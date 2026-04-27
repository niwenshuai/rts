using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public enum NavObstacleShape
    {
        Aabb,
        Circle
    }

    public readonly struct NavObstacle
    {
        public int Id { get; }
        public NavObstacleShape Shape { get; }
        public FixedVector2 Min { get; }
        public FixedVector2 Max { get; }
        public FixedVector2 Center { get; }
        public Fix64 Radius { get; }

        public NavObstacle(int id, FixedVector2 min, FixedVector2 max)
        {
            Id = id;
            Shape = NavObstacleShape.Aabb;
            Min = min;
            Max = max;
            Center = new FixedVector2((min.X + max.X) / Fix64.FromInt(2), (min.Y + max.Y) / Fix64.FromInt(2));
            Radius = Fix64.Zero;
        }

        private NavObstacle(int id, FixedVector2 center, Fix64 radius)
        {
            Id = id;
            Shape = NavObstacleShape.Circle;
            Center = center;
            Radius = radius.RawValue < 0 ? Fix64.Zero : radius;
            var delta = new FixedVector2(Radius, Radius);
            Min = center - delta;
            Max = center + delta;
        }

        public static NavObstacle Circle(int id, FixedVector2 center, Fix64 radius)
        {
            return new NavObstacle(id, center, radius);
        }

        public bool Intersects(FixedBounds2 bounds)
        {
            return Intersects(bounds, Fix64.Zero);
        }

        public bool Intersects(FixedBounds2 bounds, Fix64 agentRadius)
        {
            if (Shape == NavObstacleShape.Circle)
            {
                Fix64 radius = Radius + agentRadius;
                FixedVector2 closest = Clamp(Center, bounds);
                return (Center - closest).SqrMagnitude <= radius * radius;
            }

            Fix64 amount = agentRadius.RawValue < 0 ? Fix64.Zero : agentRadius;
            FixedVector2 min = Min - new FixedVector2(amount, amount);
            FixedVector2 max = Max + new FixedVector2(amount, amount);
            return min.X <= bounds.Max.X &&
                max.X >= bounds.Min.X &&
                min.Y <= bounds.Max.Y &&
                max.Y >= bounds.Min.Y;
        }

        public bool Contains(FixedVector2 point, Fix64 agentRadius)
        {
            if (Shape == NavObstacleShape.Circle)
            {
                Fix64 radius = Radius + agentRadius;
                return (point - Center).SqrMagnitude < radius * radius;
            }

            Fix64 amount = agentRadius.RawValue < 0 ? Fix64.Zero : agentRadius;
            return point.X > Min.X - amount &&
                point.X < Max.X + amount &&
                point.Y > Min.Y - amount &&
                point.Y < Max.Y + amount;
        }

        private static FixedVector2 Clamp(FixedVector2 point, FixedBounds2 bounds)
        {
            return new FixedVector2(
                FixedMath.Clamp(point.X, bounds.Min.X, bounds.Max.X),
                FixedMath.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y));
        }

        public NavObstacle Inflate(Fix64 amount)
        {
            if (Shape == NavObstacleShape.Circle)
            {
                return Circle(Id, Center, Radius + amount);
            }

            var delta = new FixedVector2(amount, amount);
            return new NavObstacle(Id, Min - delta, Max + delta);
        }
    }
}
