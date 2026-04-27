using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Physics
{
    public readonly struct FixedAvoidanceAgent
    {
        public int Id { get; }
        public FixedVector2 Position { get; }
        public FixedVector2 Velocity { get; }
        public Fix64 Radius { get; }

        public FixedAvoidanceAgent(int id, FixedVector2 position, FixedVector2 velocity, Fix64 radius)
        {
            Id = id;
            Position = position;
            Velocity = velocity;
            Radius = radius.RawValue < 0 ? Fix64.Zero : radius;
        }
    }

    public static class FixedCollisionAvoidance
    {
        public static FixedVector2 ResolveCirclePenetrations(
            FixedVector2 position,
            Fix64 radius,
            IReadOnlyList<FixedCircle> obstacles,
            int iterations = 2)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                return position;
            }

            FixedVector2 resolved = position;
            int count = iterations < 1 ? 1 : iterations;
            for (int iteration = 0; iteration < count; iteration++)
            {
                bool changed = false;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    var self = new FixedCircle(resolved, radius);
                    if (FixedCollision.ComputePenetration(self, obstacles[i], out FixedVector2 normal, out Fix64 depth))
                    {
                        resolved += normal * depth;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return resolved;
        }

        public static FixedVector2 ComputeSeparation(
            int selfId,
            FixedVector2 position,
            Fix64 radius,
            IReadOnlyList<FixedAvoidanceAgent> neighbors,
            Fix64 viewDistance)
        {
            if (neighbors == null || neighbors.Count == 0 || viewDistance <= Fix64.Zero)
            {
                return FixedVector2.Zero;
            }

            FixedVector2 separation = FixedVector2.Zero;
            Fix64 viewDistanceSqr = viewDistance * viewDistance;

            for (int i = 0; i < neighbors.Count; i++)
            {
                FixedAvoidanceAgent neighbor = neighbors[i];
                if (neighbor.Id == selfId)
                {
                    continue;
                }

                FixedVector2 delta = position - neighbor.Position;
                Fix64 distanceSqr = delta.SqrMagnitude;
                if (distanceSqr > viewDistanceSqr)
                {
                    continue;
                }

                FixedVector2 direction;
                Fix64 distance;
                if (distanceSqr <= Fix64.Epsilon)
                {
                    direction = selfId <= neighbor.Id
                        ? new FixedVector2(Fix64.One, Fix64.Zero)
                        : new FixedVector2(-Fix64.One, Fix64.Zero);
                    distance = Fix64.Zero;
                }
                else
                {
                    distance = FixedMath.Sqrt(distanceSqr);
                    direction = delta / distance;
                }

                Fix64 combinedRadius = radius + neighbor.Radius;
                Fix64 personalSpace = FixedMath.Max(combinedRadius, Fix64.Epsilon);
                Fix64 strength = distance < personalSpace
                    ? Fix64.One + (personalSpace - distance) / personalSpace
                    : (viewDistance - distance) / viewDistance;
                separation += direction * FixedMath.Max(strength, Fix64.Zero);
            }

            return separation;
        }

        public static FixedVector2 SteerAwayFromCircleObstacles(
            FixedVector2 position,
            FixedVector2 desiredVelocity,
            Fix64 radius,
            Fix64 lookAheadTime,
            IReadOnlyList<FixedCircle> obstacles,
            Fix64 avoidanceStrength)
        {
            if (obstacles == null || obstacles.Count == 0 || desiredVelocity.SqrMagnitude <= Fix64.Epsilon)
            {
                return desiredVelocity;
            }

            Fix64 speed = desiredVelocity.Magnitude;
            Fix64 lookAheadDistance = speed * lookAheadTime;
            if (lookAheadDistance <= Fix64.Epsilon)
            {
                return desiredVelocity;
            }

            FixedVector2 direction = desiredVelocity / speed;
            var ray = new FixedRay2(position, direction);
            FixedVector2 steering = FixedVector2.Zero;

            for (int i = 0; i < obstacles.Count; i++)
            {
                FixedCircle inflated = new FixedCircle(obstacles[i].Center, obstacles[i].Radius + radius);
                if (!FixedRaycast.Raycast(ray, inflated, lookAheadDistance, out FixedRaycastHit2 hit))
                {
                    continue;
                }

                FixedVector2 away = hit.Point - obstacles[i].Center;
                if (away.SqrMagnitude <= Fix64.Epsilon)
                {
                    away = FixedPhysicsMath.Perpendicular(direction);
                }
                else
                {
                    away = away.Normalized;
                }

                Fix64 weight = (lookAheadDistance - hit.Distance) / lookAheadDistance;
                steering += away * FixedMath.Clamp(weight, Fix64.Zero, Fix64.One);
            }

            return desiredVelocity + FixedPhysicsMath.ClampMagnitude(steering, Fix64.One) * avoidanceStrength;
        }

        public static FixedVector2 ChooseSafeVelocity(
            FixedVector2 position,
            FixedVector2 desiredVelocity,
            Fix64 radius,
            Fix64 maxSpeed,
            Fix64 horizonTime,
            IReadOnlyList<FixedCircle> circleObstacles,
            IReadOnlyList<FixedAabb2> aabbObstacles = null)
        {
            if (maxSpeed <= Fix64.Zero || desiredVelocity.SqrMagnitude <= Fix64.Epsilon)
            {
                return FixedVector2.Zero;
            }

            Fix64 speed = FixedMath.Min(desiredVelocity.Magnitude, maxSpeed);
            FixedVector2 desiredDirection = desiredVelocity.Normalized;
            FixedVector2 side = FixedPhysicsMath.Perpendicular(desiredDirection);

            FixedVector2 bestVelocity = FixedVector2.Zero;
            Fix64 bestScore = Fix64.Zero;
            bool hasBest = false;

            EvaluateCandidate(position, desiredDirection, desiredDirection, speed, radius, horizonTime, circleObstacles, aabbObstacles, ref bestVelocity, ref bestScore, ref hasBest);
            EvaluateCandidate(position, (desiredDirection + side).Normalized, desiredDirection, speed, radius, horizonTime, circleObstacles, aabbObstacles, ref bestVelocity, ref bestScore, ref hasBest);
            EvaluateCandidate(position, (desiredDirection - side).Normalized, desiredDirection, speed, radius, horizonTime, circleObstacles, aabbObstacles, ref bestVelocity, ref bestScore, ref hasBest);
            EvaluateCandidate(position, side, desiredDirection, speed, radius, horizonTime, circleObstacles, aabbObstacles, ref bestVelocity, ref bestScore, ref hasBest);
            EvaluateCandidate(position, side * -Fix64.One, desiredDirection, speed, radius, horizonTime, circleObstacles, aabbObstacles, ref bestVelocity, ref bestScore, ref hasBest);

            return hasBest ? bestVelocity : FixedVector2.Zero;
        }

        private static void EvaluateCandidate(
            FixedVector2 position,
            FixedVector2 direction,
            FixedVector2 desiredDirection,
            Fix64 speed,
            Fix64 radius,
            Fix64 horizonTime,
            IReadOnlyList<FixedCircle> circleObstacles,
            IReadOnlyList<FixedAabb2> aabbObstacles,
            ref FixedVector2 bestVelocity,
            ref Fix64 bestScore,
            ref bool hasBest)
        {
            if (direction.SqrMagnitude <= Fix64.Epsilon)
            {
                return;
            }

            direction = direction.Normalized;
            Fix64 horizonDistance = speed * horizonTime;
            Fix64 score = FixedVector2.Dot(direction, desiredDirection) * Fix64.FromInt(100);

            if (horizonDistance > Fix64.Epsilon)
            {
                var ray = new FixedRay2(position, direction);
                score -= ComputeCirclePenalty(ray, horizonDistance, radius, circleObstacles);
                score -= ComputeAabbPenalty(ray, horizonDistance, radius, aabbObstacles);
            }

            if (!hasBest || score > bestScore)
            {
                hasBest = true;
                bestScore = score;
                bestVelocity = direction * speed;
            }
        }

        private static Fix64 ComputeCirclePenalty(FixedRay2 ray, Fix64 horizonDistance, Fix64 radius, IReadOnlyList<FixedCircle> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                return Fix64.Zero;
            }

            Fix64 penalty = Fix64.Zero;
            for (int i = 0; i < obstacles.Count; i++)
            {
                FixedCircle inflated = new FixedCircle(obstacles[i].Center, obstacles[i].Radius + radius);
                if (FixedRaycast.Raycast(ray, inflated, horizonDistance, out FixedRaycastHit2 hit))
                {
                    Fix64 closeness = (horizonDistance - hit.Distance) / horizonDistance;
                    penalty += FixedMath.Clamp(closeness, Fix64.Zero, Fix64.One) * Fix64.FromInt(120);
                }
            }

            return penalty;
        }

        private static Fix64 ComputeAabbPenalty(FixedRay2 ray, Fix64 horizonDistance, Fix64 radius, IReadOnlyList<FixedAabb2> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                return Fix64.Zero;
            }

            Fix64 penalty = Fix64.Zero;
            for (int i = 0; i < obstacles.Count; i++)
            {
                FixedAabb2 inflated = obstacles[i].Inflate(radius);
                if (FixedRaycast.Raycast(ray, inflated, horizonDistance, out FixedRaycastHit2 hit))
                {
                    Fix64 closeness = (horizonDistance - hit.Distance) / horizonDistance;
                    penalty += FixedMath.Clamp(closeness, Fix64.Zero, Fix64.One) * Fix64.FromInt(120);
                }
            }

            return penalty;
        }
    }
}
