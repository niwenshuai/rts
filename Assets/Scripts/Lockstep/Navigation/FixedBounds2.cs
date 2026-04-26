using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public readonly struct FixedBounds2
    {
        public FixedVector2 Min { get; }
        public FixedVector2 Max { get; }
        public FixedVector2 Center => new FixedVector2((Min.X + Max.X) / Fix64.FromInt(2), (Min.Y + Max.Y) / Fix64.FromInt(2));

        public FixedBounds2(FixedVector2 min, FixedVector2 max)
        {
            Min = min;
            Max = max;
        }

        public bool Contains(FixedVector2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }
    }
}
