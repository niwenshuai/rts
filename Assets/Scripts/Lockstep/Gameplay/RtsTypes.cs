using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Gameplay
{
    public enum RtsEntityKind
    {
        Unit,
        Building,
        Resource
    }

    public enum RtsUnitType
    {
        None = 0,
        Worker = 1,
        Soldier = 2
    }

    public enum RtsBuildingType
    {
        None = 0,
        TownHall = 1,
        Barracks = 2,
        GuardTower = 3
    }

    public enum RtsResourceType
    {
        None = 0,
        GoldMine = 1
    }

    public enum RtsUnitOrder
    {
        Idle,
        GatherGold,
        Move,
        Attack
    }

    public enum RtsCommandType
    {
        Move = 100,
        Attack = 101,
        ProduceUnit = 102,
        BuildBuilding = 103
    }

    public static class RtsGameplayConstants
    {
        public const int PlayerCount = 2;
        public const int StartingGold = 250;
        public const int WorkerGoldCapacity = 20;
        public const int WorkerGatherAmount = 10;
        public const int GoldMineStartingGold = 5000;
        public const int GoldMineSlotCount = 5;

        public static readonly Fix64 FixedDeltaTime = Fix64.One / Fix64.FromInt(30);
        public static readonly Fix64 WorkerGatherDuration = Fix64.FromRaw(Fix64.Scale * 6 / 10);
        public static readonly Fix64 DepositRange = Fix64.FromRaw(Fix64.Scale * 26 / 10);
        public static readonly Fix64 GatherRange = Fix64.FromRaw(Fix64.Scale * 22 / 10);
        public static readonly Fix64 MineSlotArrivalRadius = Fix64.FromRaw(Fix64.Scale * 25 / 100);
        public static readonly Fix64 BuildPlacementRange = Fix64.FromInt(8);
        public static readonly Fix64 MapRadiusX = Fix64.FromInt(24);
        public static readonly Fix64 MapRadiusZ = Fix64.FromInt(15);
        public static readonly Fix64 MapCellSize = Fix64.FromInt(2);
    }
}
