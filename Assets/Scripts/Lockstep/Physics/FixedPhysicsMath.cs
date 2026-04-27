using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Physics
{
    public static class FixedPhysicsMath
    {
        public static FixedVector2 Perpendicular(FixedVector2 value)
        {
            return new FixedVector2(-value.Y, value.X);
        }

        public static Fix64 Cross(FixedVector2 a, FixedVector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        public static FixedVector2 ClampMagnitude(FixedVector2 value, Fix64 maxMagnitude)
        {
            if (maxMagnitude <= Fix64.Zero)
            {
                return FixedVector2.Zero;
            }

            Fix64 maxSqr = maxMagnitude * maxMagnitude;
            if (value.SqrMagnitude <= maxSqr)
            {
                return value;
            }

            return value.Normalized * maxMagnitude;
        }

        public static FixedVector3 ClampMagnitude(FixedVector3 value, Fix64 maxMagnitude)
        {
            if (maxMagnitude <= Fix64.Zero)
            {
                return FixedVector3.Zero;
            }

            Fix64 maxSqr = maxMagnitude * maxMagnitude;
            if (value.SqrMagnitude <= maxSqr)
            {
                return value;
            }

            return value.Normalized * maxMagnitude;
        }

        public static FixedVector2 ClampPoint(FixedVector2 point, FixedAabb2 bounds)
        {
            return new FixedVector2(
                FixedMath.Clamp(point.X, bounds.Min.X, bounds.Max.X),
                FixedMath.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y));
        }

        public static FixedVector3 ClampPoint(FixedVector3 point, FixedAabb3 bounds)
        {
            return new FixedVector3(
                FixedMath.Clamp(point.X, bounds.Min.X, bounds.Max.X),
                FixedMath.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y),
                FixedMath.Clamp(point.Z, bounds.Min.Z, bounds.Max.Z));
        }

        public static FixedVector2 ClosestPointOnSegment(FixedVector2 point, FixedVector2 start, FixedVector2 end)
        {
            FixedVector2 segment = end - start;
            Fix64 lengthSqr = segment.SqrMagnitude;
            if (lengthSqr <= Fix64.Epsilon)
            {
                return start;
            }

            Fix64 t = FixedVector2.Dot(point - start, segment) / lengthSqr;
            t = FixedMath.Clamp(t, Fix64.Zero, Fix64.One);
            return start + segment * t;
        }

        public static FixedVector3 ClosestPointOnSegment(FixedVector3 point, FixedVector3 start, FixedVector3 end)
        {
            FixedVector3 segment = end - start;
            Fix64 lengthSqr = segment.SqrMagnitude;
            if (lengthSqr <= Fix64.Epsilon)
            {
                return start;
            }

            Fix64 t = FixedVector3.Dot(point - start, segment) / lengthSqr;
            t = FixedMath.Clamp(t, Fix64.Zero, Fix64.One);
            return start + segment * t;
        }

        public static Fix64 SqrDistancePointSegment(FixedVector2 point, FixedVector2 start, FixedVector2 end)
        {
            FixedVector2 closest = ClosestPointOnSegment(point, start, end);
            return (point - closest).SqrMagnitude;
        }

        public static Fix64 SqrDistancePointSegment(FixedVector3 point, FixedVector3 start, FixedVector3 end)
        {
            FixedVector3 closest = ClosestPointOnSegment(point, start, end);
            return (point - closest).SqrMagnitude;
        }

        public static bool SegmentsIntersect(FixedVector2 aStart, FixedVector2 aEnd, FixedVector2 bStart, FixedVector2 bEnd)
        {
            FixedVector2 a = aEnd - aStart;
            FixedVector2 b = bEnd - bStart;
            Fix64 cross = Cross(a, b);
            FixedVector2 delta = bStart - aStart;

            if (FixedMath.Abs(cross) <= Fix64.Epsilon)
            {
                if (FixedMath.Abs(Cross(delta, a)) > Fix64.Epsilon)
                {
                    return false;
                }

                return Overlaps(aStart.X, aEnd.X, bStart.X, bEnd.X) &&
                    Overlaps(aStart.Y, aEnd.Y, bStart.Y, bEnd.Y);
            }

            Fix64 t = Cross(delta, b) / cross;
            Fix64 u = Cross(delta, a) / cross;
            return t >= Fix64.Zero && t <= Fix64.One && u >= Fix64.Zero && u <= Fix64.One;
        }

        public static Fix64 SqrDistanceSegmentSegment(FixedVector2 aStart, FixedVector2 aEnd, FixedVector2 bStart, FixedVector2 bEnd)
        {
            if (SegmentsIntersect(aStart, aEnd, bStart, bEnd))
            {
                return Fix64.Zero;
            }

            Fix64 d0 = SqrDistancePointSegment(aStart, bStart, bEnd);
            Fix64 d1 = SqrDistancePointSegment(aEnd, bStart, bEnd);
            Fix64 d2 = SqrDistancePointSegment(bStart, aStart, aEnd);
            Fix64 d3 = SqrDistancePointSegment(bEnd, aStart, aEnd);
            return FixedMath.Min(FixedMath.Min(d0, d1), FixedMath.Min(d2, d3));
        }

        private static bool Overlaps(Fix64 a0, Fix64 a1, Fix64 b0, Fix64 b1)
        {
            Fix64 aMin = FixedMath.Min(a0, a1);
            Fix64 aMax = FixedMath.Max(a0, a1);
            Fix64 bMin = FixedMath.Min(b0, b1);
            Fix64 bMax = FixedMath.Max(b0, b1);
            return aMin <= bMax && aMax >= bMin;
        }
    }
}
