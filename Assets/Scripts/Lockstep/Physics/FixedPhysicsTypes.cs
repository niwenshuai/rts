using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Physics
{
    public readonly struct FixedRay2
    {
        public FixedVector2 Origin { get; }
        public FixedVector2 Direction { get; }

        public FixedRay2(FixedVector2 origin, FixedVector2 direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public FixedVector2 GetPoint(Fix64 distance)
        {
            return Origin + Direction * distance;
        }
    }

    public readonly struct FixedRay3
    {
        public FixedVector3 Origin { get; }
        public FixedVector3 Direction { get; }

        public FixedRay3(FixedVector3 origin, FixedVector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public FixedVector3 GetPoint(Fix64 distance)
        {
            return Origin + Direction * distance;
        }
    }

    public readonly struct FixedAabb2
    {
        public FixedVector2 Min { get; }
        public FixedVector2 Max { get; }
        public FixedVector2 Center => (Min + Max) / Fix64.FromInt(2);
        public FixedVector2 Size => Max - Min;
        public FixedVector2 Extents => Size / Fix64.FromInt(2);

        public FixedAabb2(FixedVector2 min, FixedVector2 max)
        {
            Min = new FixedVector2(FixedMath.Min(min.X, max.X), FixedMath.Min(min.Y, max.Y));
            Max = new FixedVector2(FixedMath.Max(min.X, max.X), FixedMath.Max(min.Y, max.Y));
        }

        public static FixedAabb2 FromCenterExtents(FixedVector2 center, FixedVector2 extents)
        {
            return new FixedAabb2(center - extents, center + extents);
        }

        public bool Contains(FixedVector2 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                point.Y >= Min.Y && point.Y <= Max.Y;
        }

        public FixedAabb2 Inflate(Fix64 amount)
        {
            var delta = new FixedVector2(amount, amount);
            return new FixedAabb2(Min - delta, Max + delta);
        }
    }

    public readonly struct FixedAabb3
    {
        public FixedVector3 Min { get; }
        public FixedVector3 Max { get; }
        public FixedVector3 Center => (Min + Max) / Fix64.FromInt(2);
        public FixedVector3 Size => Max - Min;
        public FixedVector3 Extents => Size / Fix64.FromInt(2);

        public FixedAabb3(FixedVector3 min, FixedVector3 max)
        {
            Min = new FixedVector3(
                FixedMath.Min(min.X, max.X),
                FixedMath.Min(min.Y, max.Y),
                FixedMath.Min(min.Z, max.Z));
            Max = new FixedVector3(
                FixedMath.Max(min.X, max.X),
                FixedMath.Max(min.Y, max.Y),
                FixedMath.Max(min.Z, max.Z));
        }

        public static FixedAabb3 FromCenterExtents(FixedVector3 center, FixedVector3 extents)
        {
            return new FixedAabb3(center - extents, center + extents);
        }

        public bool Contains(FixedVector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                point.Y >= Min.Y && point.Y <= Max.Y &&
                point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public FixedAabb3 Inflate(Fix64 amount)
        {
            var delta = new FixedVector3(amount, amount, amount);
            return new FixedAabb3(Min - delta, Max + delta);
        }
    }

    public readonly struct FixedCircle
    {
        public FixedVector2 Center { get; }
        public Fix64 Radius { get; }

        public FixedCircle(FixedVector2 center, Fix64 radius)
        {
            Center = center;
            Radius = radius.RawValue < 0 ? Fix64.Zero : radius;
        }
    }

    public readonly struct FixedSphere
    {
        public FixedVector3 Center { get; }
        public Fix64 Radius { get; }

        public FixedSphere(FixedVector3 center, Fix64 radius)
        {
            Center = center;
            Radius = radius.RawValue < 0 ? Fix64.Zero : radius;
        }
    }

    public readonly struct FixedCapsule2
    {
        public FixedVector2 Start { get; }
        public FixedVector2 End { get; }
        public Fix64 Radius { get; }

        public FixedCapsule2(FixedVector2 start, FixedVector2 end, Fix64 radius)
        {
            Start = start;
            End = end;
            Radius = radius.RawValue < 0 ? Fix64.Zero : radius;
        }
    }

    public readonly struct FixedRaycastHit2
    {
        public FixedVector2 Point { get; }
        public FixedVector2 Normal { get; }
        public Fix64 Distance { get; }

        public FixedRaycastHit2(FixedVector2 point, FixedVector2 normal, Fix64 distance)
        {
            Point = point;
            Normal = normal;
            Distance = distance;
        }
    }

    public readonly struct FixedRaycastHit3
    {
        public FixedVector3 Point { get; }
        public FixedVector3 Normal { get; }
        public Fix64 Distance { get; }

        public FixedRaycastHit3(FixedVector3 point, FixedVector3 normal, Fix64 distance)
        {
            Point = point;
            Normal = normal;
            Distance = distance;
        }
    }
}
