using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public readonly struct NavObstacle
    {
        public int Id { get; }
        public FixedVector2 Min { get; }
        public FixedVector2 Max { get; }

        public NavObstacle(int id, FixedVector2 min, FixedVector2 max)
        {
            Id = id;
            Min = min;
            Max = max;
        }

        public bool Intersects(FixedBounds2 bounds)
        {
            return Min.X <= bounds.Max.X &&
                Max.X >= bounds.Min.X &&
                Min.Y <= bounds.Max.Y &&
                Max.Y >= bounds.Min.Y;
        }

        public NavObstacle Inflate(Fix64 amount)
        {
            var delta = new FixedVector2(amount, amount);
            return new NavObstacle(Id, Min - delta, Max + delta);
        }
    }
}
