using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class NavMeshQuery
    {
        private static readonly Fix64 DefaultSampleStep = Fix64.Half;
        private static readonly Fix64 ProjectionPadding = Fix64.FromRaw(Fix64.Scale / 20);
        private static readonly Fix64 TurnPenaltyScale = Fix64.FromRaw(Fix64.Scale / 5);
        private static readonly Fix64 ClearancePenaltyScale = Fix64.FromInt(4);

        private readonly NavMeshData _data;
        private readonly DynamicObstacleSet _dynamicObstacles;
        private readonly HashSet<int> _blockedPolygons = new HashSet<int>();
        private int _blockedVersion = -1;
        private Fix64 _blockedAgentRadius;
        private int _blockedIgnoreObstacleId;
        private bool _blockedUseDynamicObstacles;

        public NavMeshQuery(NavMeshData data, DynamicObstacleSet dynamicObstacles = null)
        {
            _data = data;
            _dynamicObstacles = dynamicObstacles ?? new DynamicObstacleSet();
        }

        public bool TryFindPath(FixedVector2 start, FixedVector2 end, List<FixedVector2> result, int maxVisited = 4096)
        {
            return TryFindPath(start, end, result, NavMeshQueryFilter.Default, maxVisited);
        }

        public bool TryFindPath(FixedVector2 start, FixedVector2 end, List<FixedVector2> result, NavMeshQueryFilter filter, int maxVisited = 4096)
        {
            result.Clear();
            RefreshBlockedPolygons(filter);

            if (!TryResolveEndpoint(start, filter, false, out NavPolygon startPolygon, out FixedVector2 resolvedStart) ||
                !TryResolveEndpoint(end, filter, true, out NavPolygon endPolygon, out FixedVector2 resolvedEnd))
            {
                return false;
            }

            if ((resolvedStart - resolvedEnd).SqrMagnitude <= Fix64.Epsilon)
            {
                result.Add(resolvedStart);
                return true;
            }

            if (IsSegmentWalkable(resolvedStart, resolvedEnd, filter))
            {
                result.Add(resolvedStart);
                result.Add(resolvedEnd);
                return true;
            }

            if (startPolygon.Id == endPolygon.Id)
            {
                return false;
            }

            var open = new BinaryHeap();
            var records = new Dictionary<int, NodeRecord>(256);
            var closed = new HashSet<int>();

            records[startPolygon.Id] = new NodeRecord(-1, Fix64.Zero, resolvedStart);
            open.Push(startPolygon.Id, Heuristic(resolvedStart, resolvedEnd));

            int visited = 0;
            while (open.Count > 0 && visited++ < maxVisited)
            {
                int currentId = open.Pop();
                if (!closed.Add(currentId))
                {
                    continue;
                }

                if (currentId == endPolygon.Id)
                {
                    BuildPath(records, endPolygon.Id, result);
                    SmoothPath(result, filter);
                    return result.Count > 1;
                }

                if (!_data.TryGetPolygon(currentId, out NavPolygon current))
                {
                    continue;
                }

                NodeRecord currentRecord = records[currentId];
                for (int i = 0; i < current.Neighbors.Count; i++)
                {
                    int neighborId = current.Neighbors[i];
                    if (closed.Contains(neighborId))
                    {
                        continue;
                    }

                    if (!_data.TryGetPolygon(neighborId, out NavPolygon neighbor) ||
                        (_blockedPolygons.Contains(neighborId) && neighborId != endPolygon.Id))
                    {
                        continue;
                    }

                    FixedVector2 neighborPoint = neighborId == endPolygon.Id ? resolvedEnd : neighbor.Center;
                    if (!IsSegmentWalkable(currentRecord.Point, neighborPoint, filter, DefaultSampleStep))
                    {
                        continue;
                    }

                    int previousId = currentId;
                    Fix64 nextCost = currentRecord.CostFromStart +
                        ComputeTraversalCost(records, currentRecord.PreviousId, currentRecord.Point, neighborPoint, neighbor, filter);

                    if (currentRecord.PreviousId >= 0 && records.TryGetValue(currentRecord.PreviousId, out NodeRecord parentRecord) &&
                        IsSegmentWalkable(parentRecord.Point, neighborPoint, filter, DefaultSampleStep))
                    {
                        Fix64 parentCost = parentRecord.CostFromStart +
                            ComputeTraversalCost(records, parentRecord.PreviousId, parentRecord.Point, neighborPoint, neighbor, filter);
                        if (parentCost <= nextCost)
                        {
                            previousId = currentRecord.PreviousId;
                            nextCost = parentCost;
                        }
                    }

                    if (records.TryGetValue(neighborId, out NodeRecord known) && known.CostFromStart <= nextCost)
                    {
                        continue;
                    }

                    records[neighborId] = new NodeRecord(previousId, nextCost, neighborPoint);
                    open.Push(neighborId, nextCost + Heuristic(neighborPoint, resolvedEnd));
                }
            }

            return false;
        }

        public void SmoothPath(List<FixedVector2> path, Fix64 sampleStep = default)
        {
            SmoothPath(path, NavMeshQueryFilter.Default, sampleStep);
        }

        public void SmoothPath(List<FixedVector2> path, NavMeshQueryFilter filter, Fix64 sampleStep = default)
        {
            if (path == null || path.Count <= 2)
            {
                return;
            }

            if (sampleStep <= Fix64.Epsilon)
            {
                sampleStep = DefaultSampleStep;
            }

            RemoveDuplicatePoints(path);

            var smoothed = new List<FixedVector2>(path.Count);
            int current = 0;
            smoothed.Add(path[current]);

            while (current < path.Count - 1)
            {
                int next = path.Count - 1;
                while (next > current + 1 && !IsSegmentWalkable(path[current], path[next], filter, sampleStep))
                {
                    next--;
                }

                smoothed.Add(path[next]);
                current = next;
            }

            path.Clear();
            path.AddRange(smoothed);
            RemoveDuplicatePoints(path);
        }

        public bool IsSegmentWalkable(FixedVector2 start, FixedVector2 end, Fix64 sampleStep = default)
        {
            return IsSegmentWalkable(start, end, NavMeshQueryFilter.Default, sampleStep);
        }

        public bool IsSegmentWalkable(FixedVector2 start, FixedVector2 end, NavMeshQueryFilter filter, Fix64 sampleStep = default)
        {
            if (sampleStep <= Fix64.Epsilon)
            {
                sampleStep = DefaultSampleStep;
            }

            RefreshBlockedPolygons(filter);
            if (!IsAgentPointWalkable(start, filter) || !IsAgentPointWalkable(end, filter))
            {
                return false;
            }

            FixedVector2 delta = end - start;
            Fix64 distance = delta.Magnitude;
            if (distance <= Fix64.Epsilon)
            {
                return true;
            }

            int steps = System.Math.Max(1, (distance / sampleStep).ToInt() + 1);
            for (int i = 1; i < steps; i++)
            {
                Fix64 t = Fix64.FromInt(i) / Fix64.FromInt(steps);
                var point = new FixedVector2(
                    FixedMath.Lerp(start.X, end.X, t),
                    FixedMath.Lerp(start.Y, end.Y, t));

                if (!IsAgentPointWalkable(point, filter))
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshBlockedPolygons(NavMeshQueryFilter filter)
        {
            if (_blockedVersion == _dynamicObstacles.Version &&
                _blockedAgentRadius == filter.AgentRadius &&
                _blockedIgnoreObstacleId == filter.IgnoreObstacleId &&
                _blockedUseDynamicObstacles == filter.UseDynamicObstacles)
            {
                return;
            }

            _blockedPolygons.Clear();
            if (filter.UseDynamicObstacles)
            {
                foreach (NavObstacle obstacle in _dynamicObstacles.Obstacles)
                {
                    if (obstacle.Id == filter.IgnoreObstacleId)
                    {
                        continue;
                    }

                    for (int i = 0; i < _data.Polygons.Count; i++)
                    {
                        NavPolygon polygon = _data.Polygons[i];
                        if (obstacle.Intersects(polygon.Bounds, filter.AgentRadius))
                        {
                            _blockedPolygons.Add(polygon.Id);
                        }
                    }
                }
            }

            _blockedVersion = _dynamicObstacles.Version;
            _blockedAgentRadius = filter.AgentRadius;
            _blockedIgnoreObstacleId = filter.IgnoreObstacleId;
            _blockedUseDynamicObstacles = filter.UseDynamicObstacles;
        }

        private bool TryResolveEndpoint(
            FixedVector2 point,
            NavMeshQueryFilter filter,
            bool allowProjection,
            out NavPolygon polygon,
            out FixedVector2 resolved)
        {
            resolved = point;
            if (TryFindContainingPolygon(point, out polygon) && IsAgentPointWalkable(point, filter))
            {
                return true;
            }

            if (!allowProjection)
            {
                return false;
            }

            return TryFindNearestWalkablePoint(point, filter, out resolved, out polygon);
        }

        private bool TryFindNearestWalkablePoint(
            FixedVector2 point,
            NavMeshQueryFilter filter,
            out FixedVector2 nearest,
            out NavPolygon nearestPolygon)
        {
            nearest = point;
            nearestPolygon = null;
            Fix64 bestScore = Fix64.Zero;
            bool hasBest = false;
            Fix64 inset = filter.AgentRadius + ProjectionPadding;

            for (int i = 0; i < _data.Polygons.Count; i++)
            {
                NavPolygon polygon = _data.Polygons[i];
                if (_blockedPolygons.Contains(polygon.Id))
                {
                    continue;
                }

                FixedVector2 candidate = ClampToInsetBounds(point, polygon.Bounds, inset);
                if (!IsAgentPointWalkable(candidate, filter))
                {
                    candidate = polygon.Center;
                    if (!IsAgentPointWalkable(candidate, filter))
                    {
                        continue;
                    }
                }

                Fix64 score = (candidate - point).SqrMagnitude;
                if (!hasBest || score < bestScore)
                {
                    hasBest = true;
                    bestScore = score;
                    nearest = candidate;
                    nearestPolygon = polygon;
                }
            }

            return hasBest;
        }

        private bool IsAgentPointWalkable(FixedVector2 point, NavMeshQueryFilter filter)
        {
            if (!TryFindContainingPolygon(point, out NavPolygon _))
            {
                return false;
            }

            if (IsPointBlocked(point, filter))
            {
                return false;
            }

            Fix64 radius = filter.AgentRadius;
            if (radius <= Fix64.Epsilon)
            {
                return true;
            }

            return IsPointOnNavMesh(point + new FixedVector2(radius, Fix64.Zero)) &&
                IsPointOnNavMesh(point - new FixedVector2(radius, Fix64.Zero)) &&
                IsPointOnNavMesh(point + new FixedVector2(Fix64.Zero, radius)) &&
                IsPointOnNavMesh(point - new FixedVector2(Fix64.Zero, radius));
        }

        private bool IsPointOnNavMesh(FixedVector2 point)
        {
            return TryFindContainingPolygon(point, out NavPolygon _);
        }

        private bool IsPointBlocked(FixedVector2 point, NavMeshQueryFilter filter)
        {
            if (!filter.UseDynamicObstacles)
            {
                return false;
            }

            foreach (NavObstacle obstacle in _dynamicObstacles.Obstacles)
            {
                if (obstacle.Id != filter.IgnoreObstacleId && obstacle.Contains(point, filter.AgentRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindContainingPolygon(FixedVector2 point, out NavPolygon polygon)
        {
            for (int i = 0; i < _data.Polygons.Count; i++)
            {
                NavPolygon candidate = _data.Polygons[i];
                if (candidate.Bounds.Contains(point))
                {
                    polygon = candidate;
                    return true;
                }
            }

            polygon = null;
            return false;
        }

        private Fix64 ComputeTraversalCost(
            Dictionary<int, NodeRecord> records,
            int previousId,
            FixedVector2 from,
            FixedVector2 to,
            NavPolygon toPolygon,
            NavMeshQueryFilter filter)
        {
            Fix64 cost = Distance(from, to);
            if (previousId >= 0 && records.TryGetValue(previousId, out NodeRecord previous))
            {
                cost += ComputeTurnPenalty(previous.Point, from, to);
            }

            cost += ComputeClearancePenalty(to, toPolygon, filter);
            return cost;
        }

        private Fix64 ComputeTurnPenalty(FixedVector2 previous, FixedVector2 current, FixedVector2 next)
        {
            FixedVector2 incoming = current - previous;
            FixedVector2 outgoing = next - current;
            if (incoming.SqrMagnitude <= Fix64.Epsilon || outgoing.SqrMagnitude <= Fix64.Epsilon)
            {
                return Fix64.Zero;
            }

            incoming = incoming.Normalized;
            outgoing = outgoing.Normalized;
            Fix64 dot = FixedMath.Clamp(FixedVector2.Dot(incoming, outgoing), -Fix64.One, Fix64.One);
            return (Fix64.One - dot) * TurnPenaltyScale;
        }

        private Fix64 ComputeClearancePenalty(FixedVector2 point, NavPolygon polygon, NavMeshQueryFilter filter)
        {
            if (!filter.UseDynamicObstacles)
            {
                return Fix64.Zero;
            }

            Fix64 comfortDistance = FixedMath.Max(filter.AgentRadius + Fix64.Half, Fix64.Half);
            Fix64 penalty = Fix64.Zero;
            foreach (NavObstacle obstacle in _dynamicObstacles.Obstacles)
            {
                if (obstacle.Id == filter.IgnoreObstacleId)
                {
                    continue;
                }

                Fix64 clearance = DistanceToObstacle(point, obstacle) - filter.AgentRadius;
                if (clearance <= Fix64.Zero)
                {
                    return Fix64.FromInt(100000);
                }

                if (clearance < comfortDistance)
                {
                    penalty += (comfortDistance - clearance) * ClearancePenaltyScale;
                }
            }

            if (_blockedPolygons.Contains(polygon.Id))
            {
                penalty += Fix64.FromInt(8);
            }

            return penalty;
        }

        private static Fix64 DistanceToObstacle(FixedVector2 point, NavObstacle obstacle)
        {
            if (obstacle.Shape == NavObstacleShape.Circle)
            {
                Fix64 distance = (point - obstacle.Center).Magnitude - obstacle.Radius;
                return FixedMath.Max(distance, Fix64.Zero);
            }

            Fix64 dx = Fix64.Zero;
            if (point.X < obstacle.Min.X)
            {
                dx = obstacle.Min.X - point.X;
            }
            else if (point.X > obstacle.Max.X)
            {
                dx = point.X - obstacle.Max.X;
            }

            Fix64 dy = Fix64.Zero;
            if (point.Y < obstacle.Min.Y)
            {
                dy = obstacle.Min.Y - point.Y;
            }
            else if (point.Y > obstacle.Max.Y)
            {
                dy = point.Y - obstacle.Max.Y;
            }

            if (dx <= Fix64.Epsilon && dy <= Fix64.Epsilon)
            {
                return Fix64.Zero;
            }

            return FixedMath.Sqrt(dx * dx + dy * dy);
        }

        private static FixedVector2 ClampToInsetBounds(FixedVector2 point, FixedBounds2 bounds, Fix64 inset)
        {
            Fix64 minX = bounds.Min.X + inset;
            Fix64 maxX = bounds.Max.X - inset;
            Fix64 minY = bounds.Min.Y + inset;
            Fix64 maxY = bounds.Max.Y - inset;

            if (minX > maxX)
            {
                Fix64 centerX = (bounds.Min.X + bounds.Max.X) / Fix64.FromInt(2);
                minX = centerX;
                maxX = centerX;
            }

            if (minY > maxY)
            {
                Fix64 centerY = (bounds.Min.Y + bounds.Max.Y) / Fix64.FromInt(2);
                minY = centerY;
                maxY = centerY;
            }

            return new FixedVector2(
                FixedMath.Clamp(point.X, minX, maxX),
                FixedMath.Clamp(point.Y, minY, maxY));
        }

        private static Fix64 Heuristic(FixedVector2 a, FixedVector2 b)
        {
            return Distance(a, b);
        }

        private static Fix64 Distance(FixedVector2 a, FixedVector2 b)
        {
            return (a - b).Magnitude;
        }

        private static void BuildPath(Dictionary<int, NodeRecord> records, int endId, List<FixedVector2> result)
        {
            var reversed = new List<FixedVector2>();
            int id = endId;
            while (id != -1 && records.TryGetValue(id, out NodeRecord record))
            {
                reversed.Add(record.Point);
                id = record.PreviousId;
            }

            for (int i = reversed.Count - 1; i >= 0; i--)
            {
                AppendPoint(result, reversed[i]);
            }
        }

        private static void RemoveDuplicatePoints(List<FixedVector2> path)
        {
            for (int i = path.Count - 1; i > 0; i--)
            {
                if ((path[i] - path[i - 1]).SqrMagnitude <= Fix64.Epsilon)
                {
                    path.RemoveAt(i);
                }
            }
        }

        private static void AppendPoint(List<FixedVector2> path, FixedVector2 point)
        {
            if (path.Count == 0 || (path[path.Count - 1] - point).SqrMagnitude > Fix64.Epsilon)
            {
                path.Add(point);
            }
        }

        private readonly struct NodeRecord
        {
            public int PreviousId { get; }
            public Fix64 CostFromStart { get; }
            public FixedVector2 Point { get; }

            public NodeRecord(int previousId, Fix64 costFromStart, FixedVector2 point)
            {
                PreviousId = previousId;
                CostFromStart = costFromStart;
                Point = point;
            }
        }
    }
}
