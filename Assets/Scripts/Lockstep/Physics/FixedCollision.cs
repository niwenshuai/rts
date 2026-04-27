using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Physics
{
    public static class FixedCollision
    {
        public static bool Intersects(FixedAabb2 a, FixedAabb2 b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }

        public static bool Intersects(FixedAabb3 a, FixedAabb3 b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        public static bool Intersects(FixedCircle a, FixedCircle b)
        {
            Fix64 radius = a.Radius + b.Radius;
            return (a.Center - b.Center).SqrMagnitude <= radius * radius;
        }

        public static bool Intersects(FixedSphere a, FixedSphere b)
        {
            Fix64 radius = a.Radius + b.Radius;
            return (a.Center - b.Center).SqrMagnitude <= radius * radius;
        }

        public static bool Intersects(FixedCircle circle, FixedAabb2 bounds)
        {
            FixedVector2 closest = FixedPhysicsMath.ClampPoint(circle.Center, bounds);
            return (circle.Center - closest).SqrMagnitude <= circle.Radius * circle.Radius;
        }

        public static bool Intersects(FixedSphere sphere, FixedAabb3 bounds)
        {
            FixedVector3 closest = FixedPhysicsMath.ClampPoint(sphere.Center, bounds);
            return (sphere.Center - closest).SqrMagnitude <= sphere.Radius * sphere.Radius;
        }

        public static bool Intersects(FixedCapsule2 capsule, FixedCircle circle)
        {
            Fix64 radius = capsule.Radius + circle.Radius;
            Fix64 distanceSqr = FixedPhysicsMath.SqrDistancePointSegment(circle.Center, capsule.Start, capsule.End);
            return distanceSqr <= radius * radius;
        }

        public static bool Intersects(FixedCapsule2 a, FixedCapsule2 b)
        {
            Fix64 radius = a.Radius + b.Radius;
            Fix64 distanceSqr = FixedPhysicsMath.SqrDistanceSegmentSegment(a.Start, a.End, b.Start, b.End);
            return distanceSqr <= radius * radius;
        }

        public static bool Intersects(FixedCapsule2 capsule, FixedAabb2 bounds)
        {
            if (bounds.Contains(capsule.Start) || bounds.Contains(capsule.End))
            {
                return true;
            }

            FixedAabb2 inflatedBounds = bounds.Inflate(capsule.Radius);
            FixedVector2 segment = capsule.End - capsule.Start;
            Fix64 segmentLength = segment.Magnitude;
            if (segmentLength <= Fix64.Epsilon)
            {
                return Intersects(new FixedCircle(capsule.Start, capsule.Radius), bounds);
            }

            var ray = new FixedRay2(capsule.Start, segment / segmentLength);
            return FixedRaycast.Raycast(ray, inflatedBounds, segmentLength, out _);
        }

        public static bool Contains(FixedCircle circle, FixedVector2 point)
        {
            return (point - circle.Center).SqrMagnitude <= circle.Radius * circle.Radius;
        }

        public static bool Contains(FixedSphere sphere, FixedVector3 point)
        {
            return (point - sphere.Center).SqrMagnitude <= sphere.Radius * sphere.Radius;
        }

        public static bool ComputePenetration(FixedCircle a, FixedCircle b, out FixedVector2 normal, out Fix64 depth)
        {
            FixedVector2 delta = a.Center - b.Center;
            Fix64 radius = a.Radius + b.Radius;
            Fix64 distanceSqr = delta.SqrMagnitude;
            if (distanceSqr > radius * radius)
            {
                normal = FixedVector2.Zero;
                depth = Fix64.Zero;
                return false;
            }

            if (distanceSqr <= Fix64.Epsilon)
            {
                normal = new FixedVector2(Fix64.One, Fix64.Zero);
                depth = radius;
                return true;
            }

            Fix64 distance = FixedMath.Sqrt(distanceSqr);
            normal = delta / distance;
            depth = radius - distance;
            return true;
        }

        public static bool ComputePenetration(FixedCircle circle, FixedAabb2 bounds, out FixedVector2 normal, out Fix64 depth)
        {
            FixedVector2 closest = FixedPhysicsMath.ClampPoint(circle.Center, bounds);
            FixedVector2 delta = circle.Center - closest;
            Fix64 distanceSqr = delta.SqrMagnitude;

            if (distanceSqr > circle.Radius * circle.Radius)
            {
                normal = FixedVector2.Zero;
                depth = Fix64.Zero;
                return false;
            }

            if (distanceSqr > Fix64.Epsilon)
            {
                Fix64 distance = FixedMath.Sqrt(distanceSqr);
                normal = delta / distance;
                depth = circle.Radius - distance;
                return true;
            }

            Fix64 left = FixedMath.Abs(circle.Center.X - bounds.Min.X);
            Fix64 right = FixedMath.Abs(bounds.Max.X - circle.Center.X);
            Fix64 bottom = FixedMath.Abs(circle.Center.Y - bounds.Min.Y);
            Fix64 top = FixedMath.Abs(bounds.Max.Y - circle.Center.Y);
            Fix64 min = FixedMath.Min(FixedMath.Min(left, right), FixedMath.Min(bottom, top));

            if (min == left)
            {
                normal = new FixedVector2(-Fix64.One, Fix64.Zero);
                depth = circle.Radius + left;
            }
            else if (min == right)
            {
                normal = new FixedVector2(Fix64.One, Fix64.Zero);
                depth = circle.Radius + right;
            }
            else if (min == bottom)
            {
                normal = new FixedVector2(Fix64.Zero, -Fix64.One);
                depth = circle.Radius + bottom;
            }
            else
            {
                normal = new FixedVector2(Fix64.Zero, Fix64.One);
                depth = circle.Radius + top;
            }

            return true;
        }
    }
}
