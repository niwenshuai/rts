using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Physics
{
    public static class FixedRaycast
    {
        public static bool Raycast(FixedRay2 ray, FixedAabb2 bounds, Fix64 maxDistance, out FixedRaycastHit2 hit)
        {
            Fix64 tMin = Fix64.Zero;
            Fix64 tMax = maxDistance;
            FixedVector2 normal = FixedVector2.Zero;

            if (!ClipAxis(ray.Origin.X, ray.Direction.X, bounds.Min.X, bounds.Max.X, new FixedVector2(-Fix64.One, Fix64.Zero), new FixedVector2(Fix64.One, Fix64.Zero), ref tMin, ref tMax, ref normal) ||
                !ClipAxis(ray.Origin.Y, ray.Direction.Y, bounds.Min.Y, bounds.Max.Y, new FixedVector2(Fix64.Zero, -Fix64.One), new FixedVector2(Fix64.Zero, Fix64.One), ref tMin, ref tMax, ref normal))
            {
                hit = default;
                return false;
            }

            hit = new FixedRaycastHit2(ray.GetPoint(tMin), normal, tMin);
            return true;
        }

        public static bool Raycast(FixedRay3 ray, FixedAabb3 bounds, Fix64 maxDistance, out FixedRaycastHit3 hit)
        {
            Fix64 tMin = Fix64.Zero;
            Fix64 tMax = maxDistance;
            FixedVector3 normal = FixedVector3.Zero;

            if (!ClipAxis(ray.Origin.X, ray.Direction.X, bounds.Min.X, bounds.Max.X, new FixedVector3(-Fix64.One, Fix64.Zero, Fix64.Zero), new FixedVector3(Fix64.One, Fix64.Zero, Fix64.Zero), ref tMin, ref tMax, ref normal) ||
                !ClipAxis(ray.Origin.Y, ray.Direction.Y, bounds.Min.Y, bounds.Max.Y, new FixedVector3(Fix64.Zero, -Fix64.One, Fix64.Zero), new FixedVector3(Fix64.Zero, Fix64.One, Fix64.Zero), ref tMin, ref tMax, ref normal) ||
                !ClipAxis(ray.Origin.Z, ray.Direction.Z, bounds.Min.Z, bounds.Max.Z, new FixedVector3(Fix64.Zero, Fix64.Zero, -Fix64.One), new FixedVector3(Fix64.Zero, Fix64.Zero, Fix64.One), ref tMin, ref tMax, ref normal))
            {
                hit = default;
                return false;
            }

            hit = new FixedRaycastHit3(ray.GetPoint(tMin), normal, tMin);
            return true;
        }

        public static bool Raycast(FixedRay2 ray, FixedCircle circle, Fix64 maxDistance, out FixedRaycastHit2 hit)
        {
            FixedVector2 originToCenter = ray.Origin - circle.Center;
            Fix64 b = FixedVector2.Dot(originToCenter, ray.Direction);
            Fix64 c = originToCenter.SqrMagnitude - circle.Radius * circle.Radius;

            if (c > Fix64.Zero && b > Fix64.Zero)
            {
                hit = default;
                return false;
            }

            Fix64 discriminant = b * b - c;
            if (discriminant < Fix64.Zero)
            {
                hit = default;
                return false;
            }

            Fix64 distance = -b - FixedMath.Sqrt(discriminant);
            if (distance < Fix64.Zero)
            {
                distance = Fix64.Zero;
            }

            if (distance > maxDistance)
            {
                hit = default;
                return false;
            }

            FixedVector2 point = ray.GetPoint(distance);
            FixedVector2 normal = (point - circle.Center).Normalized;
            hit = new FixedRaycastHit2(point, normal, distance);
            return true;
        }

        public static bool Raycast(FixedRay3 ray, FixedSphere sphere, Fix64 maxDistance, out FixedRaycastHit3 hit)
        {
            FixedVector3 originToCenter = ray.Origin - sphere.Center;
            Fix64 b = FixedVector3.Dot(originToCenter, ray.Direction);
            Fix64 c = originToCenter.SqrMagnitude - sphere.Radius * sphere.Radius;

            if (c > Fix64.Zero && b > Fix64.Zero)
            {
                hit = default;
                return false;
            }

            Fix64 discriminant = b * b - c;
            if (discriminant < Fix64.Zero)
            {
                hit = default;
                return false;
            }

            Fix64 distance = -b - FixedMath.Sqrt(discriminant);
            if (distance < Fix64.Zero)
            {
                distance = Fix64.Zero;
            }

            if (distance > maxDistance)
            {
                hit = default;
                return false;
            }

            FixedVector3 point = ray.GetPoint(distance);
            FixedVector3 normal = (point - sphere.Center).Normalized;
            hit = new FixedRaycastHit3(point, normal, distance);
            return true;
        }

        public static bool Raycast(FixedRay2 ray, FixedCapsule2 capsule, Fix64 maxDistance, out FixedRaycastHit2 hit)
        {
            bool hasHit = false;
            FixedRaycastHit2 bestHit = default;

            if (Raycast(ray, new FixedCircle(capsule.Start, capsule.Radius), maxDistance, out var startHit))
            {
                hasHit = true;
                bestHit = startHit;
            }

            if (Raycast(ray, new FixedCircle(capsule.End, capsule.Radius), maxDistance, out var endHit) &&
                (!hasHit || endHit.Distance < bestHit.Distance))
            {
                hasHit = true;
                bestHit = endHit;
            }

            FixedVector2 axis = capsule.End - capsule.Start;
            Fix64 length = axis.Magnitude;
            if (length > Fix64.Epsilon)
            {
                FixedVector2 normal = FixedPhysicsMath.Perpendicular(axis / length);
                FixedVector2 offset = normal * capsule.Radius;
                if (RaycastSegment(ray, capsule.Start + offset, capsule.End + offset, maxDistance, normal * -Fix64.One, out var sideHit) &&
                    (!hasHit || sideHit.Distance < bestHit.Distance))
                {
                    hasHit = true;
                    bestHit = sideHit;
                }

                if (RaycastSegment(ray, capsule.Start - offset, capsule.End - offset, maxDistance, normal, out sideHit) &&
                    (!hasHit || sideHit.Distance < bestHit.Distance))
                {
                    hasHit = true;
                    bestHit = sideHit;
                }
            }

            hit = bestHit;
            return hasHit;
        }

        private static bool RaycastSegment(FixedRay2 ray, FixedVector2 start, FixedVector2 end, Fix64 maxDistance, FixedVector2 normal, out FixedRaycastHit2 hit)
        {
            FixedVector2 segment = end - start;
            Fix64 denominator = FixedPhysicsMath.Cross(ray.Direction, segment);
            if (FixedMath.Abs(denominator) <= Fix64.Epsilon)
            {
                hit = default;
                return false;
            }

            FixedVector2 delta = start - ray.Origin;
            Fix64 rayDistance = FixedPhysicsMath.Cross(delta, segment) / denominator;
            Fix64 segmentTime = FixedPhysicsMath.Cross(delta, ray.Direction) / denominator;
            if (rayDistance < Fix64.Zero || rayDistance > maxDistance || segmentTime < Fix64.Zero || segmentTime > Fix64.One)
            {
                hit = default;
                return false;
            }

            hit = new FixedRaycastHit2(ray.GetPoint(rayDistance), normal, rayDistance);
            return true;
        }

        private static bool ClipAxis(
            Fix64 origin,
            Fix64 direction,
            Fix64 min,
            Fix64 max,
            FixedVector2 minNormal,
            FixedVector2 maxNormal,
            ref Fix64 tMin,
            ref Fix64 tMax,
            ref FixedVector2 normal)
        {
            if (FixedMath.Abs(direction) <= Fix64.Epsilon)
            {
                return origin >= min && origin <= max;
            }

            Fix64 t1 = (min - origin) / direction;
            Fix64 t2 = (max - origin) / direction;
            FixedVector2 nearNormal = minNormal;

            if (t1 > t2)
            {
                Fix64 temp = t1;
                t1 = t2;
                t2 = temp;
                nearNormal = maxNormal;
            }

            if (t1 > tMin)
            {
                tMin = t1;
                normal = nearNormal;
            }

            if (t2 < tMax)
            {
                tMax = t2;
            }

            return tMin <= tMax;
        }

        private static bool ClipAxis(
            Fix64 origin,
            Fix64 direction,
            Fix64 min,
            Fix64 max,
            FixedVector3 minNormal,
            FixedVector3 maxNormal,
            ref Fix64 tMin,
            ref Fix64 tMax,
            ref FixedVector3 normal)
        {
            if (FixedMath.Abs(direction) <= Fix64.Epsilon)
            {
                return origin >= min && origin <= max;
            }

            Fix64 t1 = (min - origin) / direction;
            Fix64 t2 = (max - origin) / direction;
            FixedVector3 nearNormal = minNormal;

            if (t1 > t2)
            {
                Fix64 temp = t1;
                t1 = t2;
                t2 = temp;
                nearNormal = maxNormal;
            }

            if (t1 > tMin)
            {
                tMin = t1;
                normal = nearNormal;
            }

            if (t2 < tMax)
            {
                tMax = t2;
            }

            return tMin <= tMax;
        }
    }
}
