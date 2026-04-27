using System.Collections.Generic;
using AIRTS.Lockstep.Math;
using AIRTS.Lockstep.Navigation;
using AIRTS.Lockstep.Physics;
using AIRTS.Lockstep.Shared;

namespace AIRTS.Lockstep.Gameplay
{
    public sealed class RtsSimulation
    {
        private readonly List<RtsEntity> _entities = new List<RtsEntity>();
        private readonly Dictionary<int, RtsEntity> _byId = new Dictionary<int, RtsEntity>();
        private readonly Dictionary<int, int[]> _goldMineSlotReservations = new Dictionary<int, int[]>();
        private readonly RtsPlayerState[] _players = new RtsPlayerState[RtsGameplayConstants.PlayerCount + 1];
        private readonly List<FixedVector2> _pathScratch = new List<FixedVector2>(64);
        private readonly List<NavObstacle> _navObstacleScratch = new List<NavObstacle>(64);
        private readonly List<FixedCircle> _collisionObstacleScratch = new List<FixedCircle>(64);
        private readonly List<FixedCircle> _hardCollisionObstacleScratch = new List<FixedCircle>(32);
        private readonly List<FixedAabb2> _hardAabbObstacleScratch = new List<FixedAabb2>(32);
        private readonly List<FixedAvoidanceAgent> _avoidanceAgentScratch = new List<FixedAvoidanceAgent>(32);
        private readonly List<RtsEntity> _movingUnitScratch = new List<RtsEntity>(64);
        private readonly List<FixedVector2> _crowdDesiredVelocityScratch = new List<FixedVector2>(64);
        private readonly List<FixedVector2> _crowdResolvedVelocityScratch = new List<FixedVector2>(64);
        private readonly List<FixedVector2> _crowdPositionScratch = new List<FixedVector2>(64);
        private readonly List<Fix64> _crowdRadiusScratch = new List<Fix64>(64);
        private readonly List<FixedVector2> _movePathCache = new List<FixedVector2>(64);
        private readonly DynamicObstacleSet _dynamicObstacles = new DynamicObstacleSet();
        private int _nextEntityId = 1;
        private bool _movePathCacheValid;
        private FixedVector2 _movePathCacheTarget;
        private int _movePathCacheFrame;
        private const int AvoidanceLockFrameCount = 14;
        private static readonly Fix64 CrowdVelocityBlend = Fix64.FromRaw(Fix64.Scale * 65 / 100);
        private static readonly Fix64 CrowdSeparationRange = Fix64.FromRaw(Fix64.Scale * 35 / 100);
        private static readonly Fix64 CrowdSeparationStrength = Fix64.FromRaw(Fix64.Scale * 7 / 10);
        private static readonly Fix64 CrowdPressureStrength = Fix64.FromRaw(Fix64.Scale * 45 / 100);
        private static readonly Fix64 CrowdGoalMergeDistance = Fix64.FromInt(2);
        private static readonly Fix64 CrowdSingleArrivalRadius = Fix64.FromRaw(Fix64.Scale * 2 / 10);
        private static readonly Fix64 CrowdGroupArrivalSpacing = Fix64.FromRaw(Fix64.Scale * 75 / 100);

        public IReadOnlyList<RtsEntity> Entities => _entities;
        public NavMeshData NavMeshData { get; private set; }
        public NavMeshQuery NavQuery { get; private set; }
        public int LogicFrame { get; private set; }
        public int WinnerPlayerId { get; private set; }
        public bool IsGameOver => WinnerPlayerId > 0;

        public RtsSimulation()
        {
            Reset();
        }

        public void Reset()
        {
            _entities.Clear();
            _byId.Clear();
            _goldMineSlotReservations.Clear();
            _nextEntityId = 1;
            LogicFrame = 0;
            WinnerPlayerId = 0;
            _movePathCacheValid = false;
            _movePathCacheFrame = 0;
            _movePathCache.Clear();
            _dynamicObstacles.Clear();
            NavMeshData = RtsTestMapFactory.CreateEllipseNavMesh();
            NavQuery = new NavMeshQuery(NavMeshData, _dynamicObstacles);

            for (int i = 1; i <= RtsGameplayConstants.PlayerCount; i++)
            {
                _players[i] = new RtsPlayerState(i, RtsGameplayConstants.StartingGold);
            }

            CreateStartingBase(1, new FixedVector2(Fix64.FromInt(-17), Fix64.Zero), new FixedVector2(Fix64.FromInt(-20), Fix64.FromInt(5)));
            CreateStartingBase(2, new FixedVector2(Fix64.FromInt(17), Fix64.Zero), new FixedVector2(Fix64.FromInt(20), Fix64.FromInt(-5)));
        }

        public RtsPlayerState GetPlayer(int playerId)
        {
            return playerId >= 1 && playerId < _players.Length ? _players[playerId] : null;
        }

        public bool TryGetEntity(int entityId, out RtsEntity entity)
        {
            return _byId.TryGetValue(entityId, out entity) && entity.IsAlive;
        }

        public void TickFrame(int logicFrame, IReadOnlyList<PlayerCommand> commands)
        {
            if (IsGameOver)
            {
                return;
            }

            LogicFrame = logicFrame;
            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    ApplyCommand(commands[i]);
                }
            }

            TickProduction();
            TickWorkers();
            TickCombat();
            TickMovement();
            RemoveDeadEntitiesFromLookup();
            CheckVictory();
        }

        public void ApplyLocalCommand(PlayerCommand command)
        {
            ApplyCommand(command);
        }

        private void CreateStartingBase(int playerId, FixedVector2 townPosition, FixedVector2 minePosition)
        {
            RtsEntity townHall = CreateBuilding(playerId, RtsBuildingType.TownHall, ToPosition3(townPosition));
            RtsEntity goldMine = CreateGoldMine(ToPosition3(minePosition));
            RtsEntity worker = CreateUnit(playerId, RtsUnitType.Worker, ToPosition3(townPosition + new FixedVector2(Fix64.FromInt(2), Fix64.FromInt(-2))));

            RtsPlayerState player = GetPlayer(playerId);
            player.TownHallId = townHall.Id;
            player.GoldMineId = goldMine.Id;

            ReturnWorkerToGather(worker);
        }

        private RtsEntity CreateUnit(int ownerId, RtsUnitType unitType, FixedVector3 position)
        {
            var entity = new RtsEntity
            {
                Id = _nextEntityId++,
                OwnerId = ownerId,
                Kind = RtsEntityKind.Unit,
                UnitType = unitType,
                Position = position,
                MaxHitPoints = RtsCatalog.GetUnitMaxHp(unitType)
            };
            entity.HitPoints = entity.MaxHitPoints;
            AddEntity(entity);
            return entity;
        }

        private RtsEntity CreateBuilding(int ownerId, RtsBuildingType buildingType, FixedVector3 position)
        {
            var entity = new RtsEntity
            {
                Id = _nextEntityId++,
                OwnerId = ownerId,
                Kind = RtsEntityKind.Building,
                BuildingType = buildingType,
                Position = position,
                MaxHitPoints = RtsCatalog.GetBuildingMaxHp(buildingType)
            };
            entity.HitPoints = entity.MaxHitPoints;
            AddEntity(entity);
            return entity;
        }

        private RtsEntity CreateGoldMine(FixedVector3 position)
        {
            var entity = new RtsEntity
            {
                Id = _nextEntityId++,
                OwnerId = 0,
                Kind = RtsEntityKind.Resource,
                ResourceType = RtsResourceType.GoldMine,
                Position = position,
                MaxHitPoints = 999999,
                HitPoints = 999999,
                GoldAmount = RtsGameplayConstants.GoldMineStartingGold
            };
            AddEntity(entity);
            return entity;
        }

        private void AddEntity(RtsEntity entity)
        {
            _entities.Add(entity);
            _byId[entity.Id] = entity;
        }

        private void ApplyCommand(PlayerCommand command)
        {
            switch ((RtsCommandType)command.CommandType)
            {
                case RtsCommandType.Move:
                    ApplyMoveCommand(command);
                    break;
                case RtsCommandType.Attack:
                    ApplyAttackCommand(command);
                    break;
                case RtsCommandType.ProduceUnit:
                    ApplyProduceCommand(command);
                    break;
                case RtsCommandType.BuildBuilding:
                    ApplyBuildCommand(command);
                    break;
            }
        }

        private void ApplyMoveCommand(PlayerCommand command)
        {
            if (!TryGetOwnedUnit(command.TargetId, command.PlayerId, out RtsEntity unit))
            {
                return;
            }

            FixedVector3 target = RtsCommandCodec.ReadPosition(command);
            if (!TrySetMoveCommandPath(unit, ToPosition2(target)))
            {
                return;
            }

            ReleaseGoldMineSlot(unit);
            unit.Order = RtsUnitOrder.Move;
            unit.TargetEntityId = 0;
            unit.TargetPosition = target;
        }

        private void ApplyAttackCommand(PlayerCommand command)
        {
            if (!TryGetOwnedUnit(command.TargetId, command.PlayerId, out RtsEntity unit) ||
                !_byId.TryGetValue(command.X, out RtsEntity target) ||
                !target.IsAlive ||
                target.OwnerId == command.PlayerId ||
                target.Kind == RtsEntityKind.Resource)
            {
                return;
            }

            unit.Order = RtsUnitOrder.Attack;
            ReleaseGoldMineSlot(unit);
            unit.TargetEntityId = target.Id;
            unit.TargetPosition = target.Position;
            EnsurePathToEntity(unit, target, RtsCatalog.GetAttackRange(unit) + RtsCatalog.GetEntityRadius(target));
        }

        private void ApplyProduceCommand(PlayerCommand command)
        {
            if (!_byId.TryGetValue(command.TargetId, out RtsEntity building) ||
                !building.IsAlive ||
                building.OwnerId != command.PlayerId ||
                building.Kind != RtsEntityKind.Building ||
                building.ProducingUnitType != RtsUnitType.None)
            {
                return;
            }

            var unitType = (RtsUnitType)command.X;
            if (!RtsCatalog.CanProduce(building.BuildingType, unitType))
            {
                return;
            }

            RtsPlayerState player = GetPlayer(command.PlayerId);
            int cost = RtsCatalog.GetUnitCost(unitType);
            if (player == null || player.Gold < cost)
            {
                return;
            }

            player.Gold -= cost;
            building.ProducingUnitType = unitType;
            building.ProductionRemaining = RtsCatalog.GetProductionDuration(unitType);
        }

        private void ApplyBuildCommand(PlayerCommand command)
        {
            if (!TryGetOwnedUnit(command.TargetId, command.PlayerId, out RtsEntity worker) ||
                worker.UnitType != RtsUnitType.Worker ||
                !RtsCommandCodec.TryReadBuildingType(command, out RtsBuildingType buildingType) ||
                !RtsCatalog.CanWorkerBuild(buildingType))
            {
                return;
            }

            FixedVector3 position = RtsCommandCodec.ReadPosition(command);
            FixedVector2 position2 = ToPosition2(position);
            FixedAabb2 placement = FixedAabb2.FromCenterExtents(position2, RtsCatalog.GetBuildingHalfExtents(buildingType));
            if (!IsPlacementInsideMap(placement) || !IsPlacementClear(placement, worker.Id))
            {
                return;
            }

            RtsPlayerState player = GetPlayer(command.PlayerId);
            int cost = RtsCatalog.GetBuildingCost(buildingType);
            if (player == null || player.Gold < cost)
            {
                return;
            }

            player.Gold -= cost;
            ReleaseGoldMineSlot(worker);
            CreateBuilding(command.PlayerId, buildingType, new FixedVector3(position.X, Fix64.Zero, position.Z));
            ReturnWorkerToGather(worker);
        }

        private bool TryGetOwnedUnit(int entityId, int playerId, out RtsEntity unit)
        {
            return _byId.TryGetValue(entityId, out unit) &&
                unit.IsAlive &&
                unit.OwnerId == playerId &&
                unit.Kind == RtsEntityKind.Unit;
        }

        private void TickProduction()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity building = _entities[i];
                if (!building.IsAlive || building.Kind != RtsEntityKind.Building || building.ProducingUnitType == RtsUnitType.None)
                {
                    continue;
                }

                building.ProductionRemaining -= RtsGameplayConstants.FixedDeltaTime;
                if (building.ProductionRemaining > Fix64.Zero)
                {
                    continue;
                }

                RtsUnitType unitType = building.ProducingUnitType;
                building.ProducingUnitType = RtsUnitType.None;
                building.ProductionRemaining = Fix64.Zero;
                RtsEntity unit = CreateUnit(building.OwnerId, unitType, FindSpawnPosition(building));
                if (unit.UnitType == RtsUnitType.Worker)
                {
                    ReturnWorkerToGather(unit);
                }
            }
        }

        private void TickWorkers()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity worker = _entities[i];
                if (!worker.IsAlive || worker.Kind != RtsEntityKind.Unit || worker.UnitType != RtsUnitType.Worker || worker.Order != RtsUnitOrder.GatherGold)
                {
                    continue;
                }

                TickWorkerGather(worker);
            }
        }

        private void TickWorkerGather(RtsEntity worker)
        {
            RtsPlayerState player = GetPlayer(worker.OwnerId);
            if (player == null ||
                !_byId.TryGetValue(player.GoldMineId, out RtsEntity mine) ||
                !_byId.TryGetValue(player.TownHallId, out RtsEntity townHall) ||
                !townHall.IsAlive)
            {
                ReleaseGoldMineSlot(worker);
                return;
            }

            if (worker.CarriedGold >= RtsGameplayConstants.WorkerGoldCapacity || mine.GoldAmount <= 0)
            {
                if (mine.GoldAmount <= 0)
                {
                    ReleaseGoldMineSlot(worker);
                }

                MoveWorkerToDeposit(worker, townHall, player);
                return;
            }

            if (!TryReserveGoldMineSlot(worker, mine, townHall, out int slotIndex))
            {
                worker.ClearPath();
                worker.WorkTimer = Fix64.Zero;
                worker.TargetEntityId = mine.Id;
                worker.TargetPosition = mine.Position;
                return;
            }

            FixedVector2 slotPosition = GetGoldMineSlotPosition(worker, mine, townHall, slotIndex);
            Fix64 slotArrivalSqr = RtsGameplayConstants.MineSlotArrivalRadius * RtsGameplayConstants.MineSlotArrivalRadius;
            if ((worker.Position2 - slotPosition).SqrMagnitude > slotArrivalSqr)
            {
                worker.TargetEntityId = mine.Id;
                worker.TargetPosition = ToPosition3(slotPosition);
                EnsurePathTarget(worker, slotPosition);
                return;
            }

            worker.ClearPath();
            worker.Position = ToPosition3(slotPosition);
            worker.TargetEntityId = mine.Id;
            worker.TargetPosition = ToPosition3(slotPosition);
            worker.WorkTimer += RtsGameplayConstants.FixedDeltaTime;
            if (worker.WorkTimer < RtsGameplayConstants.WorkerGatherDuration)
            {
                return;
            }

            worker.WorkTimer = Fix64.Zero;
            int amount = System.Math.Min(RtsGameplayConstants.WorkerGatherAmount, mine.GoldAmount);
            worker.CarriedGold += amount;
            mine.GoldAmount -= amount;
        }

        private void MoveWorkerToDeposit(RtsEntity worker, RtsEntity townHall, RtsPlayerState player)
        {
            if (TryGetReservedGoldMine(worker, player, out RtsEntity mine, out int slotIndex))
            {
                FixedVector2 depositPosition = GetTownHallDepositSlotPosition(worker, mine, townHall, slotIndex);
                Fix64 depositSlotArrivalSqr = RtsGameplayConstants.MineSlotArrivalRadius * RtsGameplayConstants.MineSlotArrivalRadius;
                if ((worker.Position2 - depositPosition).SqrMagnitude > depositSlotArrivalSqr)
                {
                    worker.TargetEntityId = townHall.Id;
                    worker.TargetPosition = ToPosition3(depositPosition);
                    worker.WorkTimer = Fix64.Zero;
                    EnsurePathTarget(worker, depositPosition);
                    return;
                }

                worker.ClearPath();
                worker.Position = ToPosition3(depositPosition);
                if (worker.CarriedGold > 0)
                {
                    player.Gold += worker.CarriedGold;
                    worker.CarriedGold = 0;
                }

                return;
            }

            Fix64 depositDistanceSqr = RtsGameplayConstants.DepositRange * RtsGameplayConstants.DepositRange;
            if ((worker.Position2 - townHall.Position2).SqrMagnitude > depositDistanceSqr)
            {
                worker.TargetEntityId = townHall.Id;
                worker.TargetPosition = townHall.Position;
                worker.WorkTimer = Fix64.Zero;
                EnsurePathToEntity(worker, townHall, RtsGameplayConstants.DepositRange);
                return;
            }

            worker.ClearPath();
            if (worker.CarriedGold > 0)
            {
                player.Gold += worker.CarriedGold;
                worker.CarriedGold = 0;
            }
        }

        private bool TryReserveGoldMineSlot(RtsEntity worker, RtsEntity mine, RtsEntity townHall, out int slotIndex)
        {
            slotIndex = -1;
            if (mine == null ||
                townHall == null ||
                mine.Kind != RtsEntityKind.Resource ||
                mine.ResourceType != RtsResourceType.GoldMine)
            {
                return false;
            }

            int[] reservations = GetGoldMineSlotReservations(mine.Id);
            if (worker.ReservedGoldMineId == mine.Id &&
                worker.ReservedGoldMineSlotIndex >= 0 &&
                worker.ReservedGoldMineSlotIndex < RtsGameplayConstants.GoldMineSlotCount &&
                reservations[worker.ReservedGoldMineSlotIndex] == worker.Id)
            {
                slotIndex = worker.ReservedGoldMineSlotIndex;
                return true;
            }

            ReleaseGoldMineSlot(worker);
            CleanupGoldMineSlots(mine.Id, reservations);
            for (int i = 0; i < RtsGameplayConstants.GoldMineSlotCount; i++)
            {
                if (reservations[i] != 0)
                {
                    continue;
                }

                FixedVector2 slotPosition = GetGoldMineSlotPosition(worker, mine, townHall, i);
                if (!RtsTestMapFactory.IsInsideEllipse(slotPosition))
                {
                    continue;
                }

                reservations[i] = worker.Id;
                worker.ReservedGoldMineId = mine.Id;
                worker.ReservedGoldMineSlotIndex = i;
                slotIndex = i;
                return true;
            }

            return false;
        }

        private void ReleaseGoldMineSlot(RtsEntity worker)
        {
            if (worker == null || worker.ReservedGoldMineId == 0)
            {
                return;
            }

            if (_goldMineSlotReservations.TryGetValue(worker.ReservedGoldMineId, out int[] reservations))
            {
                int slotIndex = worker.ReservedGoldMineSlotIndex;
                if (slotIndex >= 0 &&
                    slotIndex < RtsGameplayConstants.GoldMineSlotCount &&
                    reservations[slotIndex] == worker.Id)
                {
                    reservations[slotIndex] = 0;
                }
                else
                {
                    for (int i = 0; i < reservations.Length; i++)
                    {
                        if (reservations[i] == worker.Id)
                        {
                            reservations[i] = 0;
                        }
                    }
                }
            }

            worker.ReservedGoldMineId = 0;
            worker.ReservedGoldMineSlotIndex = -1;
        }

        private int[] GetGoldMineSlotReservations(int mineId)
        {
            if (!_goldMineSlotReservations.TryGetValue(mineId, out int[] reservations))
            {
                reservations = new int[RtsGameplayConstants.GoldMineSlotCount];
                _goldMineSlotReservations[mineId] = reservations;
            }

            return reservations;
        }

        private void CleanupGoldMineSlots(int mineId, int[] reservations)
        {
            for (int i = 0; i < reservations.Length; i++)
            {
                int workerId = reservations[i];
                if (workerId == 0)
                {
                    continue;
                }

                if (!_byId.TryGetValue(workerId, out RtsEntity worker) ||
                    !worker.IsAlive ||
                    worker.Kind != RtsEntityKind.Unit ||
                    worker.UnitType != RtsUnitType.Worker ||
                    worker.Order != RtsUnitOrder.GatherGold ||
                    worker.ReservedGoldMineId != mineId ||
                    worker.ReservedGoldMineSlotIndex != i ||
                    worker.TargetEntityId == 0)
                {
                    reservations[i] = 0;
                }
            }
        }

        private FixedVector2 GetGoldMineSlotPosition(RtsEntity worker, RtsEntity mine, RtsEntity townHall, int slotIndex)
        {
            FixedVector2 towardTown = townHall.Position2 - mine.Position2;
            if (towardTown.SqrMagnitude <= Fix64.Epsilon)
            {
                towardTown = worker.OwnerId == 1
                    ? new FixedVector2(-Fix64.One, Fix64.Zero)
                    : new FixedVector2(Fix64.One, Fix64.Zero);
            }
            else
            {
                towardTown = towardTown.Normalized;
            }

            FixedVector2 side = FixedPhysicsMath.Perpendicular(towardTown);
            Fix64 laneSpacing = RtsCatalog.GetEntityRadius(worker) * Fix64.FromInt(2);
            Fix64 laneOffset = GetMiningLaneOffset(slotIndex, laneSpacing);
            Fix64 slotDistance = RtsCatalog.GetEntityRadius(mine) +
                RtsCatalog.GetEntityRadius(worker) +
                Fix64.FromRaw(Fix64.Scale * 25 / 100);
            return mine.Position2 + towardTown * slotDistance + side * laneOffset;
        }

        private bool TryGetReservedGoldMine(RtsEntity worker, RtsPlayerState player, out RtsEntity mine, out int slotIndex)
        {
            mine = null;
            slotIndex = -1;
            if (player == null ||
                worker.ReservedGoldMineId != player.GoldMineId ||
                worker.ReservedGoldMineSlotIndex < 0 ||
                worker.ReservedGoldMineSlotIndex >= RtsGameplayConstants.GoldMineSlotCount ||
                !_byId.TryGetValue(worker.ReservedGoldMineId, out mine) ||
                !mine.IsAlive)
            {
                return false;
            }

            if (!_goldMineSlotReservations.TryGetValue(worker.ReservedGoldMineId, out int[] reservations) ||
                reservations[worker.ReservedGoldMineSlotIndex] != worker.Id)
            {
                mine = null;
                slotIndex = -1;
                return false;
            }

            slotIndex = worker.ReservedGoldMineSlotIndex;
            return true;
        }

        private FixedVector2 GetTownHallDepositSlotPosition(RtsEntity worker, RtsEntity mine, RtsEntity townHall, int slotIndex)
        {
            FixedAabb2 bounds = GetEntityAabb(townHall);
            FixedVector2 toMine = mine.Position2 - townHall.Position2;
            if (toMine.SqrMagnitude <= Fix64.Epsilon)
            {
                toMine = worker.OwnerId == 1
                    ? new FixedVector2(Fix64.One, Fix64.Zero)
                    : new FixedVector2(-Fix64.One, Fix64.Zero);
            }

            Fix64 margin = RtsCatalog.GetEntityRadius(worker) + GetNavigationClearance() + GetCollisionSkin();
            Fix64 workerRadius = RtsCatalog.GetEntityRadius(worker);
            if (FixedMath.Abs(toMine.X) >= FixedMath.Abs(toMine.Y))
            {
                Fix64 sideSign = toMine.X >= Fix64.Zero ? Fix64.One : -Fix64.One;
                return GetTownHallDepositSlotOnAxis(
                    bounds.Center,
                    new FixedVector2(sideSign, Fix64.Zero),
                    new FixedVector2(Fix64.Zero, Fix64.One),
                    bounds.Extents.X,
                    bounds.Extents.Y,
                    margin,
                    workerRadius,
                    slotIndex);
            }

            Fix64 forwardSign = toMine.Y >= Fix64.Zero ? Fix64.One : -Fix64.One;
            return GetTownHallDepositSlotOnAxis(
                bounds.Center,
                new FixedVector2(Fix64.Zero, forwardSign),
                new FixedVector2(Fix64.One, Fix64.Zero),
                bounds.Extents.Y,
                bounds.Extents.X,
                margin,
                workerRadius,
                slotIndex);
        }

        private FixedVector2 GetTownHallDepositSlotOnAxis(
            FixedVector2 center,
            FixedVector2 normal,
            FixedVector2 tangent,
            Fix64 normalExtent,
            Fix64 tangentExtent,
            Fix64 margin,
            Fix64 workerRadius,
            int slotIndex)
        {
            Fix64 front = normalExtent + margin;
            Fix64 laneSpacing = workerRadius * Fix64.FromInt(2);
            Fix64 laneLimit = FixedMath.Max(tangentExtent - workerRadius / Fix64.FromInt(2), Fix64.Zero);
            Fix64 laneOffset = FixedMath.Clamp(GetMiningLaneOffset(slotIndex, laneSpacing), -laneLimit, laneLimit);
            return center + normal * front + tangent * laneOffset;
        }

        private static Fix64 GetMiningLaneOffset(int slotIndex, Fix64 laneSpacing)
        {
            switch (slotIndex)
            {
                case 0:
                    return Fix64.Zero;
                case 1:
                    return laneSpacing;
                case 2:
                    return -laneSpacing;
                case 3:
                    return laneSpacing * Fix64.FromInt(2);
                default:
                    return -laneSpacing * Fix64.FromInt(2);
            }
        }

        private void TickCombat()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity attacker = _entities[i];
                if (!attacker.IsAlive || RtsCatalog.GetAttackDamage(attacker) <= 0)
                {
                    continue;
                }

                attacker.AttackTimer -= RtsGameplayConstants.FixedDeltaTime;
                RtsEntity target = ResolveAttackTarget(attacker);
                if (target == null)
                {
                    continue;
                }

                Fix64 range = RtsCatalog.GetAttackRange(attacker) + RtsCatalog.GetEntityRadius(target);
                Fix64 distanceSqr = (attacker.Position2 - target.Position2).SqrMagnitude;
                if (distanceSqr > range * range)
                {
                    if (attacker.Kind == RtsEntityKind.Unit && attacker.Order == RtsUnitOrder.Attack)
                    {
                        EnsurePathToEntity(attacker, target, range);
                    }

                    continue;
                }

                attacker.ClearPath();
                if (attacker.AttackTimer <= Fix64.Zero)
                {
                    target.HitPoints -= RtsCatalog.GetAttackDamage(attacker);
                    attacker.AttackTimer = RtsCatalog.GetAttackCooldown(attacker);
                    if (target.HitPoints <= 0)
                    {
                        target.IsAlive = false;
                        attacker.TargetEntityId = 0;
                    }
                }
            }
        }

        private RtsEntity ResolveAttackTarget(RtsEntity attacker)
        {
            if (attacker.TargetEntityId > 0 &&
                _byId.TryGetValue(attacker.TargetEntityId, out RtsEntity explicitTarget) &&
                explicitTarget.IsAlive &&
                explicitTarget.OwnerId != attacker.OwnerId &&
                explicitTarget.Kind != RtsEntityKind.Resource)
            {
                return explicitTarget;
            }

            Fix64 aggroRange = attacker.Kind == RtsEntityKind.Building ? RtsCatalog.GetAttackRange(attacker) : Fix64.FromInt(6);
            return FindNearestEnemy(attacker, aggroRange);
        }

        private RtsEntity FindNearestEnemy(RtsEntity source, Fix64 maxRange)
        {
            RtsEntity best = null;
            Fix64 bestSqr = maxRange * maxRange;
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity candidate = _entities[i];
                if (!candidate.IsAlive ||
                    candidate.OwnerId == source.OwnerId ||
                    candidate.OwnerId == 0 ||
                    candidate.Kind == RtsEntityKind.Resource)
                {
                    continue;
                }

                Fix64 distanceSqr = (source.Position2 - candidate.Position2).SqrMagnitude;
                if (distanceSqr <= bestSqr)
                {
                    best = candidate;
                    bestSqr = distanceSqr;
                }
            }

            return best;
        }

        private void TickMovement()
        {
            RefreshNavigationObstacles();
            BuildCrowdMoveSet();
            if (_movingUnitScratch.Count > 0)
            {
                ComputeCrowdDesiredVelocities();
                ResolveCrowdVelocities();
                ApplyCrowdVelocities();
            }

            TickInteractionMovement();
            ResolveUnitOverlaps();
            CompleteCrowdMovement();
        }

        private void BuildCrowdMoveSet()
        {
            _movingUnitScratch.Clear();
            _crowdDesiredVelocityScratch.Clear();
            _crowdResolvedVelocityScratch.Clear();
            _crowdPositionScratch.Clear();
            _crowdRadiusScratch.Clear();

            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity unit = _entities[i];
                if (!unit.IsAlive ||
                    unit.Kind != RtsEntityKind.Unit ||
                    unit.Order != RtsUnitOrder.Move ||
                    unit.PathIndex >= unit.Path.Count)
                {
                    continue;
                }

                _movingUnitScratch.Add(unit);
                _crowdDesiredVelocityScratch.Add(FixedVector2.Zero);
                _crowdResolvedVelocityScratch.Add(FixedVector2.Zero);
                _crowdPositionScratch.Add(unit.Position2);
                _crowdRadiusScratch.Add(RtsCatalog.GetEntityRadius(unit));
            }
        }

        private void TickInteractionMovement()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity unit = _entities[i];
                if (!unit.IsAlive ||
                    unit.Kind != RtsEntityKind.Unit ||
                    unit.Order == RtsUnitOrder.Move ||
                    unit.PathIndex >= unit.Path.Count)
                {
                    continue;
                }

                MoveAlongPath(unit);
            }
        }

        private void ComputeCrowdDesiredVelocities()
        {
            for (int i = 0; i < _movingUnitScratch.Count; i++)
            {
                RtsEntity unit = _movingUnitScratch[i];
                Fix64 radius = _crowdRadiusScratch[i];
                FixedVector2 position = unit.Position2;
                DecayAvoidanceLock(unit);
                AdvancePathCorridor(unit, position, radius);

                if (TryMarkCrowdArrival(unit, position, radius))
                {
                    _crowdDesiredVelocityScratch[i] = FixedVector2.Zero;
                    _crowdResolvedVelocityScratch[i] = FixedVector2.Zero;
                    continue;
                }

                _crowdDesiredVelocityScratch[i] = ComputeCrowdDesiredVelocity(unit, position);
            }
        }

        private FixedVector2 ComputeCrowdDesiredVelocity(RtsEntity unit, FixedVector2 position)
        {
            while (unit.PathIndex < unit.Path.Count)
            {
                FixedVector2 waypoint = unit.Path[unit.PathIndex];
                FixedVector2 delta = waypoint - position;
                Fix64 distance = delta.Magnitude;
                if (distance > Fix64.FromRaw(Fix64.Scale / 10))
                {
                    FixedVector2 pathDirection = delta / distance;
                    FixedVector2 goalDirection = pathDirection;
                    if (unit.Order == RtsUnitOrder.Move && unit.Path.Count > 0)
                    {
                        FixedVector2 goalDelta = unit.Path[unit.Path.Count - 1] - position;
                        if (goalDelta.SqrMagnitude > Fix64.Epsilon)
                        {
                            goalDirection = goalDelta.Normalized;
                        }
                    }

                    FixedVector2 blended = pathDirection * Fix64.FromRaw(Fix64.Scale * 7 / 10) +
                        goalDirection * Fix64.FromRaw(Fix64.Scale * 3 / 10);
                    if (blended.SqrMagnitude <= Fix64.Epsilon)
                    {
                        blended = pathDirection;
                    }

                    return blended.Normalized * RtsCatalog.GetUnitSpeed(unit.UnitType);
                }

                unit.PathIndex++;
            }

            return FixedVector2.Zero;
        }

        private void ResolveCrowdVelocities()
        {
            for (int i = 0; i < _movingUnitScratch.Count; i++)
            {
                RtsEntity unit = _movingUnitScratch[i];
                FixedVector2 desiredVelocity = _crowdDesiredVelocityScratch[i];
                if (desiredVelocity.SqrMagnitude <= Fix64.Epsilon)
                {
                    _crowdResolvedVelocityScratch[i] = FixedVector2.Zero;
                    continue;
                }

                Fix64 speed = RtsCatalog.GetUnitSpeed(unit.UnitType);
                FixedVector2 desiredDirection = desiredVelocity.Normalized;
                FixedVector2 separation = FixedVector2.Zero;
                FixedVector2 pressure = FixedVector2.Zero;
                FixedVector2 position = unit.Position2;
                Fix64 radius = _crowdRadiusScratch[i];

                for (int j = 0; j < _entities.Count; j++)
                {
                    RtsEntity other = _entities[j];
                    if (!other.IsAlive || other.Id == unit.Id || other.Kind != RtsEntityKind.Unit)
                    {
                        continue;
                    }

                    FixedVector2 delta = position - other.Position2;
                    Fix64 distanceSqr = delta.SqrMagnitude;
                    Fix64 otherRadius = RtsCatalog.GetEntityRadius(other);
                    Fix64 combinedRadius = radius + otherRadius;
                    Fix64 influenceRadius = combinedRadius + CrowdSeparationRange;
                    if (distanceSqr >= influenceRadius * influenceRadius)
                    {
                        continue;
                    }

                    FixedVector2 away;
                    Fix64 distance;
                    if (distanceSqr <= Fix64.Epsilon)
                    {
                        away = unit.Id < other.Id
                            ? new FixedVector2(Fix64.One, Fix64.Zero)
                            : new FixedVector2(-Fix64.One, Fix64.Zero);
                        distance = Fix64.Zero;
                    }
                    else
                    {
                        distance = FixedMath.Sqrt(distanceSqr);
                        away = delta / distance;
                    }

                    Fix64 crowding = (influenceRadius - distance) / influenceRadius;
                    separation += away * crowding;

                    FixedVector2 forwardDelta = other.Position2 - position;
                    Fix64 forwardAmount = FixedVector2.Dot(forwardDelta, desiredDirection);
                    if (forwardAmount > Fix64.Zero && forwardAmount < combinedRadius + Fix64.Half)
                    {
                        bool sameFlow = IsSameMoveFlow(unit, other);
                        Fix64 pushWeight = sameFlow ? Fix64.One : Fix64.Half;
                        pressure += desiredDirection * ((combinedRadius + Fix64.Half - forwardAmount) / (combinedRadius + Fix64.Half) * pushWeight);
                    }
                }

                FixedVector2 resolved = desiredVelocity +
                    FixedPhysicsMath.ClampMagnitude(separation, Fix64.One) * speed * CrowdSeparationStrength +
                    FixedPhysicsMath.ClampMagnitude(pressure, Fix64.One) * speed * CrowdPressureStrength;
                resolved = FixedPhysicsMath.ClampMagnitude(resolved, speed);

                FixedVector2 previousVelocity = unit.MoveVelocity;
                if (previousVelocity.SqrMagnitude > Fix64.Epsilon)
                {
                    resolved = previousVelocity * (Fix64.One - CrowdVelocityBlend) + resolved * CrowdVelocityBlend;
                    resolved = FixedPhysicsMath.ClampMagnitude(resolved, speed);
                }

                _crowdResolvedVelocityScratch[i] = resolved;
            }
        }

        private void ApplyCrowdVelocities()
        {
            for (int i = 0; i < _movingUnitScratch.Count; i++)
            {
                RtsEntity unit = _movingUnitScratch[i];
                FixedVector2 position = unit.Position2;
                FixedVector2 velocity = _crowdResolvedVelocityScratch[i];
                Fix64 radius = _crowdRadiusScratch[i];
                FixedVector2 candidate = position + velocity * RtsGameplayConstants.FixedDeltaTime;
                bool moved = TryAcceptCrowdMoveCandidate(unit, position, candidate, radius, out FixedVector2 accepted);
                if (!moved && velocity.SqrMagnitude > Fix64.Epsilon)
                {
                    moved = TryMoveCrowdAlongHardObstacle(unit, position, velocity, radius, out accepted);
                }

                if (!moved && velocity.SqrMagnitude > Fix64.Epsilon)
                {
                    FixedVector2 desiredVelocity = _crowdDesiredVelocityScratch[i];
                    candidate = position + desiredVelocity * RtsGameplayConstants.FixedDeltaTime;
                    moved = TryAcceptCrowdMoveCandidate(unit, position, candidate, radius, out accepted);
                    if (!moved && desiredVelocity.SqrMagnitude > Fix64.Epsilon)
                    {
                        moved = TryMoveCrowdAlongHardObstacle(unit, position, desiredVelocity, radius, out accepted);
                    }
                }

                if (moved)
                {
                    unit.Position = ToPosition3(accepted);
                    unit.MoveVelocity = (accepted - position) / RtsGameplayConstants.FixedDeltaTime;
                    unit.PathBlockedFrames = 0;
                }
                else
                {
                    unit.MoveVelocity = unit.MoveVelocity * Fix64.Half;
                    unit.PathBlockedFrames++;
                    if (unit.PathBlockedFrames >= 18)
                    {
                        TryRepairPath(unit);
                    }
                }

                _crowdPositionScratch[i] = unit.Position2;
            }
        }

        private bool TryAcceptCrowdMoveCandidate(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 candidate,
            Fix64 radius,
            out FixedVector2 accepted)
        {
            BuildCollisionAvoidanceSets(unit);
            accepted = candidate;
            if ((accepted - position).SqrMagnitude <= Fix64.Epsilon ||
                !RtsTestMapFactory.IsInsideEllipse(accepted) ||
                HasHardBlockingOverlap(accepted, radius))
            {
                return false;
            }

            var navFilter = new NavMeshQueryFilter(radius, unit.Id, false);
            return NavQuery.IsSegmentWalkable(position, accepted, navFilter);
        }

        private bool TryMoveCrowdAlongHardObstacle(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 velocity,
            Fix64 radius,
            out FixedVector2 accepted)
        {
            accepted = position;
            Fix64 speed = velocity.Magnitude;
            if (speed <= Fix64.Epsilon)
            {
                return false;
            }

            BuildCollisionAvoidanceSets(unit);
            FixedVector2 candidate = position + velocity * RtsGameplayConstants.FixedDeltaTime;
            if (!TryGetHardObstacleNormal(candidate, radius, out FixedVector2 normal))
            {
                return false;
            }

            FixedVector2 slideVelocity = velocity - normal * FixedVector2.Dot(velocity, normal);
            if (slideVelocity.SqrMagnitude > Fix64.Epsilon)
            {
                FixedVector2 slideDirection = slideVelocity.Normalized;
                FixedVector2 slideCandidate = position + slideDirection * speed * RtsGameplayConstants.FixedDeltaTime;
                if (TryAcceptCrowdMoveCandidate(unit, position, slideCandidate, radius, out accepted))
                {
                    return true;
                }

                FixedVector2 looseSlideCandidate = position + slideDirection * speed * Fix64.Half * RtsGameplayConstants.FixedDeltaTime;
                if (TryAcceptCrowdMoveCandidate(unit, position, looseSlideCandidate, radius, out accepted))
                {
                    return true;
                }
            }

            FixedVector2 tangent = FixedPhysicsMath.Perpendicular(normal);
            if (tangent.SqrMagnitude <= Fix64.Epsilon)
            {
                return false;
            }

            FixedVector2 preferredDirection = GetCrowdPreferredDirection(unit, position, velocity);
            FixedVector2 oppositeTangent = tangent * -Fix64.One;
            Fix64 tangentScore = FixedVector2.Dot(tangent, preferredDirection);
            Fix64 oppositeScore = FixedVector2.Dot(oppositeTangent, preferredDirection);
            if (oppositeScore > tangentScore)
            {
                tangent = oppositeTangent;
            }

            if (TryCrowdTangentCandidate(unit, position, tangent, normal, speed, radius, out accepted))
            {
                return true;
            }

            return TryCrowdTangentCandidate(unit, position, tangent * -Fix64.One, normal, speed, radius, out accepted);
        }

        private bool TryCrowdTangentCandidate(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 tangent,
            FixedVector2 normal,
            Fix64 speed,
            Fix64 radius,
            out FixedVector2 accepted)
        {
            accepted = position;
            FixedVector2 tangentCandidate = position + tangent * speed * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptCrowdMoveCandidate(unit, position, tangentCandidate, radius, out accepted))
            {
                return true;
            }

            FixedVector2 cornerDirection = (tangent * Fix64.FromInt(3) + normal).Normalized;
            if (cornerDirection.SqrMagnitude > Fix64.Epsilon)
            {
                FixedVector2 cornerCandidate = position + cornerDirection * speed * RtsGameplayConstants.FixedDeltaTime;
                if (TryAcceptCrowdMoveCandidate(unit, position, cornerCandidate, radius, out accepted))
                {
                    return true;
                }
            }

            FixedVector2 outwardCandidate = position + normal * speed * Fix64.Half * RtsGameplayConstants.FixedDeltaTime;
            return TryAcceptCrowdMoveCandidate(unit, position, outwardCandidate, radius, out accepted);
        }

        private void CompleteCrowdMovement()
        {
            for (int i = 0; i < _movingUnitScratch.Count; i++)
            {
                RtsEntity unit = _movingUnitScratch[i];
                if (!unit.IsAlive || unit.Kind != RtsEntityKind.Unit)
                {
                    continue;
                }

                FixedVector2 position = unit.Position2;
                while (unit.PathIndex < unit.Path.Count &&
                    (unit.Path[unit.PathIndex] - position).SqrMagnitude <= Fix64.FromRaw(100))
                {
                    unit.PathIndex++;
                }

                if (TryMarkCrowdArrival(unit, position, RtsCatalog.GetEntityRadius(unit)))
                {
                    unit.MoveVelocity = FixedVector2.Zero;
                }

                if (unit.PathIndex >= unit.Path.Count && unit.Order == RtsUnitOrder.Move && unit.UnitType == RtsUnitType.Worker)
                {
                    ReturnWorkerToGather(unit);
                }
            }
        }

        private bool TryMarkCrowdArrival(RtsEntity unit, FixedVector2 position, Fix64 radius)
        {
            if (unit.Path.Count == 0 || unit.PathIndex >= unit.Path.Count)
            {
                return true;
            }

            FixedVector2 goal = unit.Path[unit.Path.Count - 1];
            Fix64 arrivalRadius = unit.Order == RtsUnitOrder.Move
                ? ComputeCrowdMoveArrivalRadius(unit, radius)
                : FixedMath.Max(radius, Fix64.FromRaw(Fix64.Scale / 5));

            if ((position - goal).SqrMagnitude > arrivalRadius * arrivalRadius)
            {
                return false;
            }

            unit.PathIndex = unit.Path.Count;
            unit.PathBlockedFrames = 0;
            return true;
        }

        private Fix64 ComputeCrowdMoveArrivalRadius(RtsEntity unit, Fix64 radius)
        {
            int sharedCount = CountMovingUnitsNearGoal(unit.PathGoal);
            if (sharedCount <= 1)
            {
                return FixedMath.Max(CrowdSingleArrivalRadius, radius);
            }

            int ring = 1;
            while (ring * ring < sharedCount)
            {
                ring++;
            }

            return radius * Fix64.FromInt(2) + CrowdGroupArrivalSpacing * Fix64.FromInt(ring - 1);
        }

        private int CountMovingUnitsNearGoal(FixedVector2 goal)
        {
            int count = 0;
            Fix64 mergeSqr = CrowdGoalMergeDistance * CrowdGoalMergeDistance;
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (!entity.IsAlive ||
                    entity.Kind != RtsEntityKind.Unit ||
                    entity.Order != RtsUnitOrder.Move ||
                    entity.Path.Count == 0)
                {
                    continue;
                }

                if ((entity.PathGoal - goal).SqrMagnitude <= mergeSqr)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsSameMoveFlow(RtsEntity a, RtsEntity b)
        {
            if (a.Order != RtsUnitOrder.Move || b.Order != RtsUnitOrder.Move)
            {
                return false;
            }

            return (a.PathGoal - b.PathGoal).SqrMagnitude <= CrowdGoalMergeDistance * CrowdGoalMergeDistance;
        }

        private FixedVector2 GetCrowdPreferredDirection(RtsEntity unit, FixedVector2 position, FixedVector2 fallbackVelocity)
        {
            if (unit.PathIndex < unit.Path.Count)
            {
                FixedVector2 pathDelta = unit.Path[unit.PathIndex] - position;
                if (pathDelta.SqrMagnitude > Fix64.Epsilon)
                {
                    return pathDelta.Normalized;
                }
            }

            if (unit.Path.Count > 0)
            {
                FixedVector2 goalDelta = unit.Path[unit.Path.Count - 1] - position;
                if (goalDelta.SqrMagnitude > Fix64.Epsilon)
                {
                    return goalDelta.Normalized;
                }
            }

            return fallbackVelocity.SqrMagnitude > Fix64.Epsilon
                ? fallbackVelocity.Normalized
                : FixedVector2.Zero;
        }

        private void MoveAlongPath(RtsEntity unit)
        {
            Fix64 speed = RtsCatalog.GetUnitSpeed(unit.UnitType);
            Fix64 step = speed * RtsGameplayConstants.FixedDeltaTime;
            Fix64 radius = RtsCatalog.GetEntityRadius(unit);
            FixedVector2 position = unit.Position2;
            DecayAvoidanceLock(unit);
            AdvancePathCorridor(unit, position, radius);
            int startPathIndex = unit.PathIndex;
            FixedVector2 startPosition = position;

            while (step > Fix64.Zero && unit.PathIndex < unit.Path.Count)
            {
                FixedVector2 waypoint = unit.Path[unit.PathIndex];
                FixedVector2 delta = waypoint - position;
                Fix64 distance = delta.Magnitude;
                if (distance <= Fix64.Epsilon)
                {
                    unit.PathIndex++;
                    continue;
                }

                Fix64 moveDistance = FixedMath.Min(step, distance);
                FixedVector2 desiredVelocity = delta / distance * (moveDistance / RtsGameplayConstants.FixedDeltaTime);
                FixedVector2 nextPosition = MoveWithCollisionAvoidance(unit, position, desiredVelocity);
                if ((nextPosition - position).SqrMagnitude <= Fix64.Epsilon)
                {
                    break;
                }

                position = nextPosition;
                if ((waypoint - position).SqrMagnitude <= Fix64.FromRaw(100))
                {
                    position = waypoint;
                    unit.PathIndex++;
                }

                step = Fix64.Zero;
            }

            bool madeProgress = (position - startPosition).SqrMagnitude > Fix64.Epsilon || unit.PathIndex != startPathIndex;
            if (madeProgress)
            {
                unit.PathBlockedFrames = 0;
            }
            else if (unit.PathIndex < unit.Path.Count)
            {
                if (!TryAcceptCongestedMoveArrival(unit, position, radius))
                {
                    unit.PathBlockedFrames++;
                    if (unit.PathBlockedFrames >= 10)
                    {
                        TryRepairPath(unit);
                    }
                }
            }

            unit.Position = ToPosition3(position);
        }

        private void AdvancePathCorridor(RtsEntity unit, FixedVector2 position, Fix64 radius)
        {
            if (unit.PathIndex + 1 >= unit.Path.Count)
            {
                return;
            }

            var navFilter = new NavMeshQueryFilter(radius, unit.Id);
            for (int i = unit.Path.Count - 1; i > unit.PathIndex; i--)
            {
                if (NavQuery.IsSegmentWalkable(position, unit.Path[i], navFilter))
                {
                    unit.PathIndex = i;
                    return;
                }
            }
        }

        private void TryRepairPath(RtsEntity unit)
        {
            Fix64 radius = RtsCatalog.GetEntityRadius(unit);
            if (TryAcceptCongestedMoveArrival(unit, unit.Position2, radius))
            {
                return;
            }

            FixedVector2 goal = unit.Path.Count > 0
                ? unit.Path[unit.Path.Count - 1]
                : unit.PathGoal;
            TrySetPath(unit, goal);
            unit.PathBlockedFrames = 0;
        }

        private FixedVector2 MoveWithCollisionAvoidance(RtsEntity unit, FixedVector2 position, FixedVector2 desiredVelocity)
        {
            if (desiredVelocity.SqrMagnitude <= Fix64.Epsilon)
            {
                return position;
            }

            Fix64 speed = desiredVelocity.Magnitude;
            Fix64 radius = RtsCatalog.GetEntityRadius(unit);
            BuildCollisionAvoidanceSets(unit);
            if (HasHardBlockingOverlap(position, radius))
            {
                FixedVector2 resolvedPosition = ResolveHardPenetrations(position, radius, 4);
                if ((resolvedPosition - position).SqrMagnitude > Fix64.Epsilon &&
                    RtsTestMapFactory.IsInsideEllipse(resolvedPosition) &&
                    !HasHardBlockingOverlap(resolvedPosition, radius))
                {
                    return resolvedPosition;
                }
            }

            FixedVector2 desiredDirection = desiredVelocity / speed;
            FixedVector2 rightSide = FixedPhysicsMath.Perpendicular(desiredDirection);

            FixedVector2 directCandidate = position + desiredVelocity * RtsGameplayConstants.FixedDeltaTime;
            if (IsNearMoveGoal(unit, position, radius) &&
                IsUnitCrowdingPoint(unit.Id, directCandidate, radius, GetCollisionSkin()) &&
                IsUnitCrowdingPoint(unit.Id, unit.Path[unit.Path.Count - 1], radius, GetNavigationClearance()))
            {
                return position;
            }

            if (TryGetBlockingUnitOverlap(unit, position, directCandidate, radius, GetCollisionSkin(), out FixedAvoidanceAgent directBlocker))
            {
                if (TryMoveAroundUnitBlocker(unit, position, desiredVelocity, radius, directBlocker, out FixedVector2 acceptedBlockerAvoidance))
                {
                    return acceptedBlockerAvoidance;
                }

                if (ShouldYieldToUnit(unit, directBlocker.Id))
                {
                    return position;
                }
            }

            if (TryAcceptMoveCandidate(unit, position, directCandidate, radius, false, out FixedVector2 acceptedDirect))
            {
                return acceptedDirect;
            }

            if (TryMoveAlongHardObstacle(unit, position, desiredVelocity, radius, out FixedVector2 acceptedSlide))
            {
                return acceptedSlide;
            }

            FixedVector2 diagonalVelocity = (desiredDirection + rightSide).Normalized * speed;
            FixedVector2 diagonalCandidate = position + diagonalVelocity * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptMoveCandidate(unit, position, diagonalCandidate, radius, true, out FixedVector2 acceptedDiagonal))
            {
                return acceptedDiagonal;
            }

            FixedVector2 sideCandidate = position + rightSide * speed * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptMoveCandidate(unit, position, sideCandidate, radius, true, out FixedVector2 acceptedSide))
            {
                return acceptedSide;
            }

            FixedVector2 leftSide = rightSide * -Fix64.One;
            FixedVector2 leftDiagonalVelocity = (desiredDirection + leftSide).Normalized * speed;
            FixedVector2 leftDiagonalCandidate = position + leftDiagonalVelocity * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptMoveCandidate(unit, position, leftDiagonalCandidate, radius, true, out FixedVector2 acceptedLeftDiagonal))
            {
                return acceptedLeftDiagonal;
            }

            FixedVector2 leftSideCandidate = position + leftSide * speed * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptMoveCandidate(unit, position, leftSideCandidate, radius, true, out FixedVector2 acceptedLeftSide))
            {
                return acceptedLeftSide;
            }

            FixedVector2 separation = FixedCollisionAvoidance.ComputeSeparation(
                unit.Id,
                position,
                radius,
                _avoidanceAgentScratch,
                radius + Fix64.FromInt(3));

            FixedVector2 steeredVelocity = desiredVelocity;
            if (separation.SqrMagnitude > Fix64.Epsilon)
            {
                FixedVector2 avoidance = FixedPhysicsMath.ClampMagnitude(separation, Fix64.One);
                if (FixedVector2.Dot(avoidance, desiredDirection) < -Fix64.Half)
                {
                    avoidance = (avoidance + rightSide).Normalized;
                }

                steeredVelocity += avoidance * speed;
            }

            if (steeredVelocity.SqrMagnitude <= Fix64.Epsilon)
            {
                steeredVelocity = rightSide * speed;
            }

            FixedVector2 safeVelocity = FixedCollisionAvoidance.ChooseSafeVelocity(
                position,
                steeredVelocity,
                radius,
                speed,
                Fix64.Half,
                _collisionObstacleScratch,
                _hardAabbObstacleScratch);

            if (safeVelocity.SqrMagnitude <= Fix64.Epsilon)
            {
                return position;
            }

            FixedVector2 candidate = position + safeVelocity * RtsGameplayConstants.FixedDeltaTime;
            return TryAcceptMoveCandidate(unit, position, candidate, radius, true, out FixedVector2 accepted)
                ? accepted
                : position;
        }

        private bool TryAcceptCongestedMoveArrival(RtsEntity unit, FixedVector2 position, Fix64 radius)
        {
            if (!IsNearMoveGoal(unit, position, radius))
            {
                return false;
            }

            FixedVector2 goal = unit.Path[unit.Path.Count - 1];
            if (!IsUnitCrowdingPoint(unit.Id, goal, radius, GetNavigationClearance()))
            {
                return false;
            }

            unit.PathIndex = unit.Path.Count;
            unit.PathBlockedFrames = 0;
            return true;
        }

        private bool IsNearMoveGoal(RtsEntity unit, FixedVector2 position, Fix64 radius)
        {
            if (unit.Order != RtsUnitOrder.Move || unit.Path.Count == 0)
            {
                return false;
            }

            FixedVector2 goal = unit.Path[unit.Path.Count - 1];
            Fix64 arrivalDistance = FixedMath.Max(
                radius * Fix64.FromInt(4),
                Fix64.FromRaw(Fix64.Scale * 16 / 10));
            return (position - goal).SqrMagnitude <= arrivalDistance * arrivalDistance;
        }

        private bool IsUnitCrowdingPoint(int selfId, FixedVector2 point, Fix64 radius, Fix64 padding)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity other = _entities[i];
                if (!other.IsAlive || other.Id == selfId || other.Kind != RtsEntityKind.Unit)
                {
                    continue;
                }

                Fix64 combined = radius + RtsCatalog.GetEntityRadius(other) + padding;
                if ((point - other.Position2).SqrMagnitude < combined * combined)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAcceptMoveCandidate(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 candidate,
            Fix64 radius,
            bool resolvePenetration,
            out FixedVector2 accepted)
        {
            accepted = resolvePenetration
                ? ResolveHardPenetrations(candidate, radius, 3)
                : candidate;

            if ((accepted - position).SqrMagnitude <= Fix64.Epsilon ||
                !RtsTestMapFactory.IsInsideEllipse(accepted) ||
                HasHardBlockingOverlap(accepted, radius) ||
                HasBlockingUnitOverlap(unit, position, accepted, radius))
            {
                return false;
            }

            var navFilter = new NavMeshQueryFilter(radius, unit.Id, false);
            return NavQuery.IsSegmentWalkable(position, accepted, navFilter);
        }

        private bool TryGetBlockingUnitOverlap(
            RtsEntity unit,
            FixedVector2 previous,
            FixedVector2 candidate,
            Fix64 radius,
            Fix64 padding,
            out FixedAvoidanceAgent blocker)
        {
            Fix64 tolerance = Fix64.FromRaw(Fix64.Scale / 100);
            for (int i = 0; i < _avoidanceAgentScratch.Count; i++)
            {
                FixedAvoidanceAgent agent = _avoidanceAgentScratch[i];
                Fix64 combinedRadius = radius + agent.Radius + padding;
                Fix64 combinedSqr = combinedRadius * combinedRadius;
                Fix64 previousSqr = (previous - agent.Position).SqrMagnitude;
                Fix64 candidateSqr = (candidate - agent.Position).SqrMagnitude;
                if (candidateSqr >= combinedSqr)
                {
                    continue;
                }

                bool alreadyOverlapping = previousSqr < combinedSqr;
                bool separating = candidateSqr + tolerance >= previousSqr;
                if (alreadyOverlapping && separating)
                {
                    continue;
                }

                blocker = agent;
                return true;
            }

            blocker = default;
            return false;
        }

        private bool ShouldYieldToUnit(RtsEntity unit, int blockerId)
        {
            if (unit.Id < blockerId)
            {
                return false;
            }

            return _byId.TryGetValue(blockerId, out RtsEntity blocker) &&
                blocker.IsAlive &&
                blocker.Kind == RtsEntityKind.Unit &&
                blocker.PathIndex < blocker.Path.Count;
        }

        private bool TryMoveAroundUnitBlocker(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 desiredVelocity,
            Fix64 radius,
            FixedAvoidanceAgent blocker,
            out FixedVector2 accepted)
        {
            accepted = position;
            Fix64 speed = desiredVelocity.Magnitude;
            if (speed <= Fix64.Epsilon)
            {
                return false;
            }

            FixedVector2 desiredDirection = desiredVelocity / speed;
            bool hasLockedSide = unit.AvoidanceBlockerId == blocker.Id &&
                unit.AvoidanceLockFrames > 0 &&
                unit.AvoidanceSide != 0;
            int sideSign = GetOrCreateAvoidanceSide(unit, blocker.Id, desiredDirection, blocker.Position - position);
            FixedVector2 side = GetSignedPassingSide(desiredDirection, sideSign);
            FixedVector2 away = position - blocker.Position;
            if (away.SqrMagnitude <= Fix64.Epsilon)
            {
                away = side;
            }
            else
            {
                away = away.Normalized;
            }

            if (TryUnitAvoidanceCandidate(unit, position, (side * Fix64.FromInt(2) + desiredDirection).Normalized, speed, radius, blocker, out accepted) ||
                TryUnitAvoidanceCandidate(unit, position, side, speed, radius, blocker, out accepted) ||
                TryUnitAvoidanceCandidate(unit, position, (side + away).Normalized, speed, radius, blocker, out accepted))
            {
                SetAvoidanceLock(unit, blocker.Id, sideSign);
                return true;
            }

            if (hasLockedSide)
            {
                return false;
            }

            int oppositeSign = -sideSign;
            FixedVector2 oppositeSide = GetSignedPassingSide(desiredDirection, oppositeSign);
            if (TryUnitAvoidanceCandidate(unit, position, (oppositeSide * Fix64.FromInt(2) + desiredDirection).Normalized, speed, radius, blocker, out accepted) ||
                TryUnitAvoidanceCandidate(unit, position, oppositeSide, speed, radius, blocker, out accepted))
            {
                SetAvoidanceLock(unit, blocker.Id, oppositeSign);
                return true;
            }

            return false;
        }

        private bool TryUnitAvoidanceCandidate(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 direction,
            Fix64 speed,
            Fix64 radius,
            FixedAvoidanceAgent blocker,
            out FixedVector2 accepted)
        {
            accepted = position;
            if (direction.SqrMagnitude <= Fix64.Epsilon)
            {
                return false;
            }

            FixedVector2 candidate = position + direction.Normalized * speed * RtsGameplayConstants.FixedDeltaTime;
            Fix64 previousSqr = (position - blocker.Position).SqrMagnitude;
            Fix64 candidateSqr = (candidate - blocker.Position).SqrMagnitude;
            if (candidateSqr < previousSqr && candidateSqr > Fix64.Epsilon)
            {
                return false;
            }

            return TryAcceptMoveCandidate(unit, position, candidate, radius, true, out accepted);
        }

        private int GetOrCreateAvoidanceSide(RtsEntity unit, int blockerId, FixedVector2 desiredDirection, FixedVector2 blockerDelta)
        {
            if (unit.AvoidanceBlockerId == blockerId &&
                unit.AvoidanceLockFrames > 0 &&
                unit.AvoidanceSide != 0)
            {
                unit.AvoidanceLockFrames = AvoidanceLockFrameCount;
                return unit.AvoidanceSide;
            }

            int sideSign = ChooseDeterministicPassingSide(unit.Id, blockerId, desiredDirection, blockerDelta);
            SetAvoidanceLock(unit, blockerId, sideSign);
            return sideSign;
        }

        private void SetAvoidanceLock(RtsEntity unit, int blockerId, int sideSign)
        {
            unit.AvoidanceBlockerId = blockerId;
            unit.AvoidanceSide = sideSign < 0 ? -1 : 1;
            unit.AvoidanceLockFrames = AvoidanceLockFrameCount;
        }

        private static void ClearAvoidanceLock(RtsEntity unit)
        {
            unit.AvoidanceBlockerId = 0;
            unit.AvoidanceSide = 0;
            unit.AvoidanceLockFrames = 0;
        }

        private static void DecayAvoidanceLock(RtsEntity unit)
        {
            if (unit.AvoidanceLockFrames <= 0)
            {
                unit.AvoidanceBlockerId = 0;
                unit.AvoidanceSide = 0;
                return;
            }

            unit.AvoidanceLockFrames--;
            if (unit.AvoidanceLockFrames == 0)
            {
                unit.AvoidanceBlockerId = 0;
                unit.AvoidanceSide = 0;
            }
        }

        private static FixedVector2 GetSignedPassingSide(FixedVector2 desiredDirection, int sideSign)
        {
            FixedVector2 side = FixedPhysicsMath.Perpendicular(desiredDirection);
            if (side.SqrMagnitude <= Fix64.Epsilon)
            {
                side = new FixedVector2(Fix64.Zero, Fix64.One);
            }

            return sideSign < 0 ? side * -Fix64.One : side;
        }

        private static int ChooseDeterministicPassingSide(int unitId, int blockerId, FixedVector2 desiredDirection, FixedVector2 blockerDelta)
        {
            Fix64 cross = FixedPhysicsMath.Cross(desiredDirection, blockerDelta);
            if (FixedMath.Abs(cross) > Fix64.FromRaw(Fix64.Scale / 20))
            {
                return cross < Fix64.Zero ? -1 : 1;
            }

            return ((unitId + blockerId) & 1) == 0 ? 1 : -1;
        }

        private bool TryMoveAlongHardObstacle(
            RtsEntity unit,
            FixedVector2 position,
            FixedVector2 desiredVelocity,
            Fix64 radius,
            out FixedVector2 accepted)
        {
            accepted = position;
            FixedVector2 candidate = position + desiredVelocity * RtsGameplayConstants.FixedDeltaTime;
            if (!TryGetHardObstacleNormal(candidate, radius, out FixedVector2 normal))
            {
                return false;
            }

            FixedVector2 slideVelocity = desiredVelocity - normal * FixedVector2.Dot(desiredVelocity, normal);
            Fix64 speed = desiredVelocity.Magnitude;
            if (slideVelocity.SqrMagnitude > Fix64.Epsilon)
            {
                slideVelocity = FixedPhysicsMath.ClampMagnitude(slideVelocity, speed);
                FixedVector2 slideCandidate = position + slideVelocity * RtsGameplayConstants.FixedDeltaTime;
                if (TryAcceptMoveCandidate(unit, position, slideCandidate, radius, true, out accepted))
                {
                    return true;
                }
            }

            FixedVector2 tangent = FixedPhysicsMath.Perpendicular(normal);
            if (tangent.SqrMagnitude <= Fix64.Epsilon)
            {
                return false;
            }

            if (FixedVector2.Dot(tangent, desiredVelocity) < Fix64.Zero)
            {
                tangent = tangent * -Fix64.One;
            }

            FixedVector2 tangentCandidate = position + tangent * speed * RtsGameplayConstants.FixedDeltaTime;
            if (TryAcceptMoveCandidate(unit, position, tangentCandidate, radius, true, out accepted))
            {
                return true;
            }

            FixedVector2 reverseTangent = tangent * -Fix64.One;
            FixedVector2 reverseCandidate = position + reverseTangent * speed * RtsGameplayConstants.FixedDeltaTime;
            return TryAcceptMoveCandidate(unit, position, reverseCandidate, radius, true, out accepted);
        }

        private bool TryGetHardObstacleNormal(FixedVector2 position, Fix64 radius, out FixedVector2 normal)
        {
            var circle = new FixedCircle(position, radius);
            Fix64 bestDepth = Fix64.Zero;
            normal = FixedVector2.Zero;
            bool found = false;

            for (int i = 0; i < _hardCollisionObstacleScratch.Count; i++)
            {
                if (FixedCollision.ComputePenetration(circle, _hardCollisionObstacleScratch[i], out FixedVector2 candidateNormal, out Fix64 depth) &&
                    depth > bestDepth)
                {
                    normal = candidateNormal;
                    bestDepth = depth;
                    found = true;
                }
            }

            for (int i = 0; i < _hardAabbObstacleScratch.Count; i++)
            {
                if (FixedCollision.ComputePenetration(circle, _hardAabbObstacleScratch[i], out FixedVector2 candidateNormal, out Fix64 depth) &&
                    depth > bestDepth)
                {
                    normal = candidateNormal;
                    bestDepth = depth;
                    found = true;
                }
            }

            return found && normal.SqrMagnitude > Fix64.Epsilon;
        }

        private bool HasHardBlockingOverlap(FixedVector2 position, Fix64 radius)
        {
            var unitCircle = new FixedCircle(position, radius);
            for (int i = 0; i < _hardCollisionObstacleScratch.Count; i++)
            {
                FixedCircle obstacle = _hardCollisionObstacleScratch[i];
                Fix64 combinedRadius = radius + obstacle.Radius;
                if ((position - obstacle.Center).SqrMagnitude < combinedRadius * combinedRadius)
                {
                    return true;
                }
            }

            for (int i = 0; i < _hardAabbObstacleScratch.Count; i++)
            {
                if (IsCircleOverlappingAabb(position, radius, _hardAabbObstacleScratch[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCircleOverlappingAabb(FixedVector2 position, Fix64 radius, FixedAabb2 bounds)
        {
            FixedVector2 closest = FixedPhysicsMath.ClampPoint(position, bounds);
            return (position - closest).SqrMagnitude < radius * radius;
        }

        private FixedVector2 ResolveHardPenetrations(FixedVector2 position, Fix64 radius, int iterations)
        {
            FixedVector2 resolved = FixedCollisionAvoidance.ResolveCirclePenetrations(position, radius, _hardCollisionObstacleScratch, iterations);
            Fix64 skin = GetCollisionSkin();
            int count = iterations < 1 ? 1 : iterations;
            for (int iteration = 0; iteration < count; iteration++)
            {
                bool changed = false;
                for (int i = 0; i < _hardAabbObstacleScratch.Count; i++)
                {
                    var circle = new FixedCircle(resolved, radius);
                    if (FixedCollision.ComputePenetration(circle, _hardAabbObstacleScratch[i], out FixedVector2 normal, out Fix64 depth))
                    {
                        if (depth > Fix64.Epsilon)
                        {
                            resolved += normal * (depth + skin);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return resolved;
        }

        private bool HasBlockingUnitOverlap(RtsEntity unit, FixedVector2 previous, FixedVector2 candidate, Fix64 radius)
        {
            return TryGetBlockingUnitOverlap(unit, previous, candidate, radius, GetCollisionSkin(), out FixedAvoidanceAgent _);
        }

        private void ResolveUnitOverlaps()
        {
            RefreshNavigationObstacles();
            Fix64 padding = Fix64.FromRaw(Fix64.Scale / 50);
            for (int iteration = 0; iteration < 3; iteration++)
            {
                bool changed = false;
                for (int i = 0; i < _entities.Count; i++)
                {
                    RtsEntity a = _entities[i];
                    if (!a.IsAlive || a.Kind != RtsEntityKind.Unit)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < _entities.Count; j++)
                    {
                        RtsEntity b = _entities[j];
                        if (!b.IsAlive || b.Kind != RtsEntityKind.Unit)
                        {
                            continue;
                        }

                        if (ShouldIgnoreMiningWorkerCollision(a, b))
                        {
                            continue;
                        }

                        Fix64 radiusA = RtsCatalog.GetEntityRadius(a);
                        Fix64 radiusB = RtsCatalog.GetEntityRadius(b);
                        Fix64 targetDistance = radiusA + radiusB + padding;
                        FixedVector2 delta = a.Position2 - b.Position2;
                        Fix64 distanceSqr = delta.SqrMagnitude;
                        if (distanceSqr >= targetDistance * targetDistance)
                        {
                            continue;
                        }

                        FixedVector2 direction;
                        Fix64 distance;
                        if (distanceSqr <= Fix64.Epsilon)
                        {
                            direction = ((a.Id + b.Id) & 1) == 0
                                ? new FixedVector2(Fix64.One, Fix64.Zero)
                                : new FixedVector2(Fix64.Zero, Fix64.One);
                            distance = Fix64.Zero;
                        }
                        else
                        {
                            distance = FixedMath.Sqrt(distanceSqr);
                            direction = delta / distance;
                        }

                        Fix64 depth = targetDistance - distance;
                        FixedVector2 moveA = direction * (depth / Fix64.FromInt(2));
                        FixedVector2 moveB = moveA * -Fix64.One;
                        bool movedA = TryMoveUnitBySeparation(a, moveA, radiusA);
                        bool movedB = TryMoveUnitBySeparation(b, moveB, radiusB);
                        if (!movedA)
                        {
                            movedB |= TryMoveUnitBySeparation(b, moveB * Fix64.FromInt(2), radiusB);
                        }

                        if (!movedB)
                        {
                            movedA |= TryMoveUnitBySeparation(a, moveA * Fix64.FromInt(2), radiusA);
                        }

                        changed |= movedA || movedB;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private bool TryMoveUnitBySeparation(RtsEntity unit, FixedVector2 offset, Fix64 radius)
        {
            if (offset.SqrMagnitude <= Fix64.Epsilon)
            {
                return false;
            }

            FixedVector2 position = unit.Position2;
            FixedVector2 candidate = position + offset;
            if (!RtsTestMapFactory.IsInsideEllipse(candidate) || HasHardEntityOverlap(unit.Id, candidate, radius))
            {
                return false;
            }

            var navFilter = new NavMeshQueryFilter(radius, unit.Id, false);
            if (!NavQuery.IsSegmentWalkable(position, candidate, navFilter))
            {
                return false;
            }

            unit.Position = ToPosition3(candidate);
            return true;
        }

        private bool HasHardEntityOverlap(int selfId, FixedVector2 position, Fix64 radius)
        {
            var unitCircle = new FixedCircle(position, radius);
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (!entity.IsAlive || entity.Id == selfId || entity.Kind == RtsEntityKind.Unit)
                {
                    continue;
                }

                if (entity.Kind == RtsEntityKind.Building)
                {
                    if (IsCircleOverlappingAabb(position, radius, GetEntityAabb(entity)))
                    {
                        return true;
                    }

                    continue;
                }

                Fix64 combinedRadius = radius + RtsCatalog.GetEntityRadius(entity);
                if ((position - entity.Position2).SqrMagnitude < combinedRadius * combinedRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildCollisionAvoidanceSets(RtsEntity unit)
        {
            _collisionObstacleScratch.Clear();
            _hardCollisionObstacleScratch.Clear();
            _hardAabbObstacleScratch.Clear();
            _avoidanceAgentScratch.Clear();

            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity other = _entities[i];
                if (!other.IsAlive || other.Id == unit.Id)
                {
                    continue;
                }

                Fix64 otherRadius = RtsCatalog.GetEntityRadius(other);
                if (other.Kind == RtsEntityKind.Unit)
                {
                    if (ShouldIgnoreMiningWorkerCollision(unit, other))
                    {
                        continue;
                    }

                    _collisionObstacleScratch.Add(new FixedCircle(other.Position2, otherRadius));
                    _avoidanceAgentScratch.Add(new FixedAvoidanceAgent(other.Id, other.Position2, FixedVector2.Zero, otherRadius));
                    continue;
                }

                if (other.Kind == RtsEntityKind.Building)
                {
                    _hardAabbObstacleScratch.Add(GetEntityAabb(other));
                    continue;
                }

                var circle = new FixedCircle(other.Position2, otherRadius);
                _collisionObstacleScratch.Add(circle);
                _hardCollisionObstacleScratch.Add(circle);
            }
        }

        private static bool ShouldIgnoreMiningWorkerCollision(RtsEntity a, RtsEntity b)
        {
            return a.Kind == RtsEntityKind.Unit &&
                b.Kind == RtsEntityKind.Unit &&
                a.UnitType == RtsUnitType.Worker &&
                b.UnitType == RtsUnitType.Worker &&
                a.Order == RtsUnitOrder.GatherGold &&
                b.Order == RtsUnitOrder.GatherGold;
        }

        private void RefreshNavigationObstacles()
        {
            _navObstacleScratch.Clear();
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (!entity.IsAlive)
                {
                    continue;
                }

                if (entity.Kind == RtsEntityKind.Unit)
                {
                    continue;
                }

                if (entity.Kind == RtsEntityKind.Building)
                {
                    FixedAabb2 bounds = GetEntityAabb(entity);
                    _navObstacleScratch.Add(new NavObstacle(entity.Id, bounds.Min, bounds.Max));
                    continue;
                }

                _navObstacleScratch.Add(NavObstacle.Circle(entity.Id, entity.Position2, RtsCatalog.GetEntityRadius(entity)));
            }

            _dynamicObstacles.ReplaceAll(_navObstacleScratch);
        }

        private void EnsurePathToEntity(RtsEntity unit, RtsEntity target, Fix64 desiredRange)
        {
            if (HasValidPathToEntity(unit, target, desiredRange))
            {
                return;
            }

            EnsurePathTarget(unit, FindApproachPoint(unit, target, desiredRange));
        }

        private bool HasValidPathToEntity(RtsEntity unit, RtsEntity target, Fix64 desiredRange)
        {
            if (unit.Path.Count == 0 || unit.PathIndex >= unit.Path.Count)
            {
                return false;
            }

            FixedVector2 finalTarget = unit.Path[unit.Path.Count - 1];
            if (!RtsTestMapFactory.IsInsideEllipse(finalTarget))
            {
                return false;
            }

            if (target.Kind == RtsEntityKind.Building)
            {
                return IsValidBuildingApproachPoint(unit, target, finalTarget, desiredRange);
            }

            Fix64 clearance = GetNavigationClearance();
            Fix64 combinedRadius = RtsCatalog.GetEntityRadius(unit) + RtsCatalog.GetEntityRadius(target);
            Fix64 minDistance = combinedRadius + clearance;
            Fix64 maxDistance = FixedMath.Max(desiredRange + Fix64.Half, minDistance + Fix64.Half);
            Fix64 distanceSqr = (finalTarget - target.Position2).SqrMagnitude;
            return distanceSqr >= minDistance * minDistance && distanceSqr <= maxDistance * maxDistance;
        }

        private FixedVector2 FindApproachPoint(RtsEntity unit, RtsEntity target, Fix64 desiredRange)
        {
            if (target.Kind == RtsEntityKind.Building)
            {
                FixedVector2 buildingBest = unit.Position2;
                Fix64 buildingBestScore = Fix64.Zero;
                bool hasBuildingBest = false;
                ConsiderBuildingApproachCandidates(unit, target, desiredRange, ref buildingBest, ref buildingBestScore, ref hasBuildingBest);
                if (hasBuildingBest)
                {
                    return buildingBest;
                }
            }

            Fix64 margin = GetNavigationClearance();
            Fix64 minimumRange = RtsCatalog.GetEntityRadius(unit) + RtsCatalog.GetEntityRadius(target) + margin;
            Fix64 range = FixedMath.Max(minimumRange, desiredRange - margin);
            FixedVector2 direction = unit.Position2 - target.Position2;
            if (direction.SqrMagnitude <= Fix64.Epsilon)
            {
                direction = unit.OwnerId <= target.OwnerId
                    ? new FixedVector2(Fix64.One, Fix64.Zero)
                    : new FixedVector2(-Fix64.One, Fix64.Zero);
            }
            else
            {
                direction = direction.Normalized;
            }

            FixedVector2 best = unit.Position2;
            Fix64 bestScore = Fix64.Zero;
            bool hasBest = false;
            FixedVector2 side = FixedPhysicsMath.Perpendicular(direction);
            ConsiderApproachDirection(unit, target, range, direction, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, (direction + side).Normalized, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, (direction - side).Normalized, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, side, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, side * -Fix64.One, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, direction * -Fix64.One, ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, new FixedVector2(Fix64.One, Fix64.Zero), ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, new FixedVector2(-Fix64.One, Fix64.Zero), ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, new FixedVector2(Fix64.Zero, Fix64.One), ref best, ref bestScore, ref hasBest);
            ConsiderApproachDirection(unit, target, range, new FixedVector2(Fix64.Zero, -Fix64.One), ref best, ref bestScore, ref hasBest);
            return best;
        }

        private void ConsiderBuildingApproachCandidates(
            RtsEntity unit,
            RtsEntity target,
            Fix64 desiredRange,
            ref FixedVector2 best,
            ref Fix64 bestScore,
            ref bool hasBest)
        {
            FixedAabb2 bounds = GetEntityAabb(target);
            FixedVector2 center = bounds.Center;
            Fix64 unitRadius = RtsCatalog.GetEntityRadius(unit);
            Fix64 clearance = GetNavigationClearance();
            Fix64 margin = unitRadius + clearance + GetCollisionSkin();
            Fix64 xInset = FixedMath.Min(bounds.Extents.X, unitRadius + clearance);
            Fix64 yInset = FixedMath.Min(bounds.Extents.Y, unitRadius + clearance);
            Fix64 minSlotX = bounds.Min.X + xInset;
            Fix64 maxSlotX = bounds.Max.X - xInset;
            Fix64 minSlotY = bounds.Min.Y + yInset;
            Fix64 maxSlotY = bounds.Max.Y - yInset;
            Fix64 preferredX = FixedMath.Clamp(unit.Position2.X, minSlotX, maxSlotX);
            Fix64 preferredY = FixedMath.Clamp(unit.Position2.Y, minSlotY, maxSlotY);

            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Min.X - margin, preferredY), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Max.X + margin, preferredY), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(preferredX, bounds.Min.Y - margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(preferredX, bounds.Max.Y + margin), ref best, ref bestScore, ref hasBest);

            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Min.X - margin, center.Y), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Min.X - margin, minSlotY), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Min.X - margin, maxSlotY), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Max.X + margin, center.Y), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Max.X + margin, minSlotY), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(bounds.Max.X + margin, maxSlotY), ref best, ref bestScore, ref hasBest);

            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(center.X, bounds.Min.Y - margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(minSlotX, bounds.Min.Y - margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(maxSlotX, bounds.Min.Y - margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(center.X, bounds.Max.Y + margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(minSlotX, bounds.Max.Y + margin), ref best, ref bestScore, ref hasBest);
            ConsiderBuildingApproachCandidate(unit, target, desiredRange, new FixedVector2(maxSlotX, bounds.Max.Y + margin), ref best, ref bestScore, ref hasBest);
        }

        private void ConsiderBuildingApproachCandidate(
            RtsEntity unit,
            RtsEntity target,
            Fix64 desiredRange,
            FixedVector2 candidate,
            ref FixedVector2 best,
            ref Fix64 bestScore,
            ref bool hasBest)
        {
            Fix64 unitRadius = RtsCatalog.GetEntityRadius(unit);
            if (!IsValidBuildingApproachPoint(unit, target, candidate, desiredRange))
            {
                return;
            }

            Fix64 score = (candidate - unit.Position2).SqrMagnitude + GetApproachOccupancyPenalty(unit, candidate, unitRadius);
            if (!hasBest || score < bestScore)
            {
                best = candidate;
                bestScore = score;
                hasBest = true;
            }
        }

        private bool IsValidBuildingApproachPoint(RtsEntity unit, RtsEntity target, FixedVector2 candidate, Fix64 desiredRange)
        {
            Fix64 unitRadius = RtsCatalog.GetEntityRadius(unit);
            if (!RtsTestMapFactory.IsInsideEllipse(candidate) || HasHardEntityOverlap(unit.Id, candidate, unitRadius))
            {
                return false;
            }

            FixedAabb2 bounds = GetEntityAabb(target);
            bool xInside = candidate.X >= bounds.Min.X && candidate.X <= bounds.Max.X;
            bool yInside = candidate.Y >= bounds.Min.Y && candidate.Y <= bounds.Max.Y;
            if (xInside == yInside)
            {
                return false;
            }

            Fix64 slotInset = unitRadius + GetNavigationClearance();
            Fix64 xInset = FixedMath.Min(bounds.Extents.X, slotInset);
            Fix64 yInset = FixedMath.Min(bounds.Extents.Y, slotInset);
            if (!xInside &&
                (candidate.Y < bounds.Min.Y + yInset || candidate.Y > bounds.Max.Y - yInset))
            {
                return false;
            }

            if (!yInside &&
                (candidate.X < bounds.Min.X + xInset || candidate.X > bounds.Max.X - xInset))
            {
                return false;
            }

            FixedVector2 closest = FixedPhysicsMath.ClampPoint(candidate, bounds);
            Fix64 edgeDistanceSqr = (candidate - closest).SqrMagnitude;
            Fix64 minEdgeDistance = unitRadius + GetCollisionSkin();
            Fix64 maxEdgeDistance = unitRadius + GetNavigationClearance() + Fix64.Half;
            Fix64 maxCenterDistance = desiredRange + Fix64.Half;
            return edgeDistanceSqr >= minEdgeDistance * minEdgeDistance &&
                edgeDistanceSqr <= maxEdgeDistance * maxEdgeDistance &&
                (candidate - target.Position2).SqrMagnitude <= maxCenterDistance * maxCenterDistance;
        }

        private void ConsiderApproachDirection(
            RtsEntity unit,
            RtsEntity target,
            Fix64 range,
            FixedVector2 direction,
            ref FixedVector2 best,
            ref Fix64 bestScore,
            ref bool hasBest)
        {
            if (direction.SqrMagnitude <= Fix64.Epsilon)
            {
                return;
            }

            direction = direction.Normalized;
            Fix64 unitRadius = RtsCatalog.GetEntityRadius(unit);
            FixedVector2 candidate = target.Position2 + direction * range;
            if (!RtsTestMapFactory.IsInsideEllipse(candidate) || HasHardEntityOverlap(unit.Id, candidate, unitRadius))
            {
                return;
            }

            Fix64 score = (candidate - unit.Position2).SqrMagnitude + GetApproachOccupancyPenalty(unit, candidate, unitRadius);
            if (!hasBest || score < bestScore)
            {
                best = candidate;
                bestScore = score;
                hasBest = true;
            }
        }

        private Fix64 GetApproachOccupancyPenalty(RtsEntity unit, FixedVector2 candidate, Fix64 unitRadius)
        {
            Fix64 penalty = Fix64.Zero;
            Fix64 clearance = GetNavigationClearance();
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity other = _entities[i];
                if (!other.IsAlive || other.Id == unit.Id || other.Kind != RtsEntityKind.Unit)
                {
                    continue;
                }

                Fix64 occupiedRadius = unitRadius + RtsCatalog.GetEntityRadius(other) + clearance;
                Fix64 occupiedSqr = occupiedRadius * occupiedRadius;
                Fix64 distanceSqr = (candidate - other.Position2).SqrMagnitude;
                if (distanceSqr < occupiedSqr)
                {
                    penalty += Fix64.FromInt(1000) + occupiedSqr - distanceSqr;
                }
            }

            return penalty;
        }

        private static Fix64 GetNavigationClearance()
        {
            return Fix64.FromRaw(Fix64.Scale / 10);
        }

        private static Fix64 GetCollisionSkin()
        {
            return Fix64.FromRaw(Fix64.Scale / 50);
        }

        private void EnsurePathTarget(RtsEntity unit, FixedVector2 target)
        {
            if (unit.Path.Count > 0 && unit.PathIndex < unit.Path.Count)
            {
                FixedVector2 currentTarget = unit.Path[unit.Path.Count - 1];
                if ((currentTarget - target).SqrMagnitude <= Fix64.Half * Fix64.Half)
                {
                    return;
                }
            }

            TrySetPath(unit, target);
        }

        private bool TrySetMoveCommandPath(RtsEntity unit, FixedVector2 target)
        {
            if (_movePathCacheValid &&
                _movePathCacheFrame == LogicFrame &&
                (_movePathCacheTarget - target).SqrMagnitude <= Fix64.Epsilon &&
                TrySetPathFromMoveCache(unit, target))
            {
                return true;
            }

            if (!TrySetPath(unit, target))
            {
                return false;
            }

            _movePathCacheValid = true;
            _movePathCacheFrame = LogicFrame;
            _movePathCacheTarget = target;
            _movePathCache.Clear();
            _movePathCache.AddRange(unit.Path);
            return true;
        }

        private bool TrySetPathFromMoveCache(RtsEntity unit, FixedVector2 target)
        {
            if (_movePathCache.Count < 2)
            {
                return false;
            }

            var filter = new NavMeshQueryFilter(RtsCatalog.GetEntityRadius(unit), 0);
            FixedVector2 position = unit.Position2;
            int attachIndex = -1;
            for (int i = _movePathCache.Count - 1; i >= 1; i--)
            {
                if (NavQuery.IsSegmentWalkable(position, _movePathCache[i], filter))
                {
                    attachIndex = i;
                    break;
                }
            }

            if (attachIndex < 0)
            {
                return false;
            }

            unit.Path.Clear();
            unit.Path.Add(position);
            for (int i = attachIndex; i < _movePathCache.Count; i++)
            {
                if ((unit.Path[unit.Path.Count - 1] - _movePathCache[i]).SqrMagnitude > Fix64.Epsilon)
                {
                    unit.Path.Add(_movePathCache[i]);
                }
            }

            unit.PathIndex = 0;
            unit.PathGoal = target;
            unit.PathBlockedFrames = 0;
            ClearAvoidanceLock(unit);
            return true;
        }

        private bool TrySetPath(RtsEntity unit, FixedVector2 target)
        {
            if (!RtsTestMapFactory.IsInsideEllipse(target))
            {
                return false;
            }

            _pathScratch.Clear();
            RefreshNavigationObstacles();
            var filter = new NavMeshQueryFilter(RtsCatalog.GetEntityRadius(unit), unit.Id);
            if (!NavQuery.TryFindPath(unit.Position2, target, _pathScratch, filter))
            {
                return false;
            }

            unit.Path.Clear();
            unit.Path.AddRange(_pathScratch);
            unit.PathIndex = 0;
            unit.PathGoal = target;
            unit.PathBlockedFrames = 0;
            ClearAvoidanceLock(unit);
            return true;
        }

        private void ReturnWorkerToGather(RtsEntity worker)
        {
            RtsPlayerState player = GetPlayer(worker.OwnerId);
            if (player == null)
            {
                ReleaseGoldMineSlot(worker);
                return;
            }

            worker.Order = RtsUnitOrder.GatherGold;
            worker.TargetEntityId = player.GoldMineId;
            worker.WorkTimer = Fix64.Zero;
            if (_byId.TryGetValue(player.GoldMineId, out RtsEntity mine) &&
                _byId.TryGetValue(player.TownHallId, out RtsEntity townHall) &&
                townHall.IsAlive)
            {
                if (TryReserveGoldMineSlot(worker, mine, townHall, out int slotIndex))
                {
                    FixedVector2 slotPosition = GetGoldMineSlotPosition(worker, mine, townHall, slotIndex);
                    worker.TargetPosition = ToPosition3(slotPosition);
                    EnsurePathTarget(worker, slotPosition);
                    return;
                }

                worker.ClearPath();
                worker.TargetPosition = mine.Position;
                return;
            }

            ReleaseGoldMineSlot(worker);
            worker.ClearPath();
        }

        private FixedVector3 FindSpawnPosition(RtsEntity building)
        {
            Fix64 direction = building.OwnerId == 1 ? Fix64.One : -Fix64.One;
            FixedVector2 basePoint = building.Position2 + new FixedVector2(direction * Fix64.FromInt(3), Fix64.FromInt(-2));
            if (RtsTestMapFactory.IsInsideEllipse(basePoint))
            {
                return ToPosition3(basePoint);
            }

            return building.Position;
        }

        private bool IsPlacementInsideMap(FixedAabb2 placement)
        {
            return RtsTestMapFactory.IsInsideEllipse(placement.Min) &&
                RtsTestMapFactory.IsInsideEllipse(placement.Max) &&
                RtsTestMapFactory.IsInsideEllipse(new FixedVector2(placement.Min.X, placement.Max.Y)) &&
                RtsTestMapFactory.IsInsideEllipse(new FixedVector2(placement.Max.X, placement.Min.Y));
        }

        private bool IsPlacementClear(FixedAabb2 placement, int ignoreEntityId = 0)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (!entity.IsAlive || entity.Id == ignoreEntityId)
                {
                    continue;
                }

                if (entity.Kind == RtsEntityKind.Building)
                {
                    if (FixedCollision.Intersects(placement, GetEntityAabb(entity)))
                    {
                        return false;
                    }

                    continue;
                }

                if (FixedCollision.Intersects(new FixedCircle(entity.Position2, RtsCatalog.GetEntityRadius(entity)), placement))
                {
                    return false;
                }
            }

            return true;
        }

        private static FixedAabb2 GetEntityAabb(RtsEntity entity)
        {
            return FixedAabb2.FromCenterExtents(entity.Position2, RtsCatalog.GetBuildingHalfExtents(entity.BuildingType));
        }

        private void RemoveDeadEntitiesFromLookup()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (!entity.IsAlive)
                {
                    if (entity.Kind == RtsEntityKind.Unit && entity.UnitType == RtsUnitType.Worker)
                    {
                        ReleaseGoldMineSlot(entity);
                    }

                    _byId.Remove(entity.Id);
                }
            }
        }

        private void CheckVictory()
        {
            int aliveOwner = 0;
            for (int playerId = 1; playerId <= RtsGameplayConstants.PlayerCount; playerId++)
            {
                bool hasBuilding = HasAliveBuilding(playerId);
                _players[playerId].IsDefeated = !hasBuilding;
                if (hasBuilding)
                {
                    if (aliveOwner != 0)
                    {
                        return;
                    }

                    aliveOwner = playerId;
                }
            }

            WinnerPlayerId = aliveOwner;
        }

        private bool HasAliveBuilding(int playerId)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                RtsEntity entity = _entities[i];
                if (entity.IsAlive && entity.OwnerId == playerId && entity.Kind == RtsEntityKind.Building)
                {
                    return true;
                }
            }

            return false;
        }

        public static FixedVector2 ToPosition2(FixedVector3 position)
        {
            return new FixedVector2(position.X, position.Z);
        }

        public static FixedVector3 ToPosition3(FixedVector2 position)
        {
            return new FixedVector3(position.X, Fix64.Zero, position.Y);
        }
    }
}
