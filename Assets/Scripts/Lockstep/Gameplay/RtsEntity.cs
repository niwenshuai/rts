using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Gameplay
{
    public sealed class RtsEntity
    {
        public int Id;
        public int OwnerId;
        public RtsEntityKind Kind;
        public RtsUnitType UnitType;
        public RtsBuildingType BuildingType;
        public RtsResourceType ResourceType;
        public FixedVector3 Position;
        public int HitPoints;
        public int MaxHitPoints;
        public bool IsAlive = true;

        public RtsUnitOrder Order;
        public int TargetEntityId;
        public FixedVector3 TargetPosition;
        public int CarriedGold;
        public Fix64 WorkTimer;
        public Fix64 AttackTimer;
        public RtsUnitType ProducingUnitType;
        public Fix64 ProductionRemaining;
        public int GoldAmount;

        public readonly List<FixedVector2> Path = new List<FixedVector2>();
        public int PathIndex;
        public FixedVector2 PathGoal;
        public int PathBlockedFrames;
        public int AvoidanceBlockerId;
        public int AvoidanceSide;
        public int AvoidanceLockFrames;
        public FixedVector2 MoveVelocity;
        public int ReservedGoldMineId;
        public int ReservedGoldMineSlotIndex;

        public bool IsSelectable => IsAlive && Kind != RtsEntityKind.Resource;
        public bool IsBuilding => Kind == RtsEntityKind.Building;
        public bool IsUnit => Kind == RtsEntityKind.Unit;
        public bool IsResource => Kind == RtsEntityKind.Resource;

        public FixedVector2 Position2 => new FixedVector2(Position.X, Position.Z);

        public string DisplayName
        {
            get
            {
                if (Kind == RtsEntityKind.Unit)
                {
                    return RtsCatalog.GetUnitName(UnitType);
                }

                if (Kind == RtsEntityKind.Building)
                {
                    return RtsCatalog.GetBuildingName(BuildingType);
                }

                return "Gold Mine";
            }
        }

        public void ClearPath()
        {
            Path.Clear();
            PathIndex = 0;
            PathGoal = FixedVector2.Zero;
            PathBlockedFrames = 0;
            AvoidanceBlockerId = 0;
            AvoidanceSide = 0;
            AvoidanceLockFrames = 0;
            MoveVelocity = FixedVector2.Zero;
        }
    }
}
