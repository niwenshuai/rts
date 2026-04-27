using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Gameplay
{
    public static class RtsCatalog
    {
        public static string GetUnitName(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return "Worker";
                case RtsUnitType.Soldier:
                    return "Soldier";
                default:
                    return "Unit";
            }
        }

        public static string GetBuildingName(RtsBuildingType type)
        {
            switch (type)
            {
                case RtsBuildingType.TownHall:
                    return "Town Hall";
                case RtsBuildingType.Barracks:
                    return "Barracks";
                case RtsBuildingType.GuardTower:
                    return "Guard Tower";
                default:
                    return "Building";
            }
        }

        public static int GetUnitCost(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return 50;
                case RtsUnitType.Soldier:
                    return 80;
                default:
                    return 0;
            }
        }

        public static int GetBuildingCost(RtsBuildingType type)
        {
            switch (type)
            {
                case RtsBuildingType.Barracks:
                    return 150;
                case RtsBuildingType.GuardTower:
                    return 120;
                default:
                    return 0;
            }
        }

        public static int GetUnitMaxHp(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return 70;
                case RtsUnitType.Soldier:
                    return 120;
                default:
                    return 1;
            }
        }

        public static int GetBuildingMaxHp(RtsBuildingType type)
        {
            switch (type)
            {
                case RtsBuildingType.TownHall:
                    return 900;
                case RtsBuildingType.Barracks:
                    return 520;
                case RtsBuildingType.GuardTower:
                    return 360;
                default:
                    return 1;
            }
        }

        public static Fix64 GetUnitSpeed(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return Fix64.FromInt(4);
                case RtsUnitType.Soldier:
                    return Fix64.FromRaw(Fix64.Scale * 35 / 10);
                default:
                    return Fix64.Zero;
            }
        }

        public static Fix64 GetEntityRadius(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Unit)
            {
                return entity.UnitType == RtsUnitType.Worker
                    ? Fix64.FromRaw(Fix64.Scale * 35 / 100)
                    : Fix64.FromRaw(Fix64.Scale * 45 / 100);
            }

            if (entity.Kind == RtsEntityKind.Building)
            {
                switch (entity.BuildingType)
                {
                    case RtsBuildingType.TownHall:
                        return Fix64.FromRaw(Fix64.Scale * 18 / 10);
                    case RtsBuildingType.Barracks:
                        return Fix64.FromRaw(Fix64.Scale * 15 / 10);
                    case RtsBuildingType.GuardTower:
                        return Fix64.FromInt(1);
                }
            }

            return Fix64.FromRaw(Fix64.Scale * 12 / 10);
        }

        public static FixedVector2 GetBuildingHalfExtents(RtsBuildingType type)
        {
            switch (type)
            {
                case RtsBuildingType.TownHall:
                    return new FixedVector2(
                        Fix64.FromRaw(Fix64.Scale * 16 / 10),
                        Fix64.FromRaw(Fix64.Scale * 16 / 10));
                case RtsBuildingType.Barracks:
                    return new FixedVector2(
                        Fix64.FromRaw(Fix64.Scale * 14 / 10),
                        Fix64.FromRaw(Fix64.Scale * 14 / 10));
                case RtsBuildingType.GuardTower:
                    return new FixedVector2(
                        Fix64.FromRaw(Fix64.Scale * 9 / 10),
                        Fix64.FromRaw(Fix64.Scale * 9 / 10));
                default:
                    return FixedVector2.One;
            }
        }

        public static Fix64 GetProductionDuration(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return Fix64.FromInt(4);
                case RtsUnitType.Soldier:
                    return Fix64.FromInt(5);
                default:
                    return Fix64.Zero;
            }
        }

        public static bool CanProduce(RtsBuildingType buildingType, RtsUnitType unitType)
        {
            return (buildingType == RtsBuildingType.TownHall && unitType == RtsUnitType.Worker) ||
                (buildingType == RtsBuildingType.Barracks && unitType == RtsUnitType.Soldier);
        }

        public static bool CanWorkerBuild(RtsBuildingType buildingType)
        {
            return buildingType == RtsBuildingType.Barracks ||
                buildingType == RtsBuildingType.GuardTower;
        }

        public static Fix64 GetAttackRange(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Unit && entity.UnitType == RtsUnitType.Soldier)
            {
                return Fix64.FromRaw(Fix64.Scale * 15 / 10);
            }

            if (entity.Kind == RtsEntityKind.Building && entity.BuildingType == RtsBuildingType.GuardTower)
            {
                return Fix64.FromInt(7);
            }

            return Fix64.Zero;
        }

        public static int GetAttackDamage(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Unit && entity.UnitType == RtsUnitType.Soldier)
            {
                return 14;
            }

            if (entity.Kind == RtsEntityKind.Building && entity.BuildingType == RtsBuildingType.GuardTower)
            {
                return 20;
            }

            return 0;
        }

        public static Fix64 GetAttackCooldown(RtsEntity entity)
        {
            if (GetAttackDamage(entity) <= 0)
            {
                return Fix64.Zero;
            }

            return Fix64.FromInt(1);
        }
    }
}
