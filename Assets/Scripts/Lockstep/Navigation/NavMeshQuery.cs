using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class NavMeshQuery
    {
        private readonly NavMeshData _data;
        private readonly DynamicObstacleSet _dynamicObstacles;
        private readonly HashSet<int> _blockedPolygons = new HashSet<int>();
        private int _blockedVersion = -1;

        public NavMeshQuery(NavMeshData data, DynamicObstacleSet dynamicObstacles = null)
        {
            _data = data;
            _dynamicObstacles = dynamicObstacles ?? new DynamicObstacleSet();
        }

        public bool TryFindPath(FixedVector2 start, FixedVector2 end, List<FixedVector2> result, int maxVisited = 4096)
        {
            result.Clear();
            NavPolygon startPolygon;
            NavPolygon endPolygon;
            if (!TryFindContainingPolygon(start, out startPolygon) || !TryFindContainingPolygon(end, out endPolygon))
            {
                return false;
            }

            RefreshBlockedPolygons();
            if (_blockedPolygons.Contains(startPolygon.Id) || _blockedPolygons.Contains(endPolygon.Id))
            {
                return false;
            }

            if (startPolygon.Id == endPolygon.Id)
            {
                result.Add(start);
                result.Add(end);
                return true;
            }

            var open = new BinaryHeap();
            var records = new Dictionary<int, NodeRecord>(256);
            records[startPolygon.Id] = new NodeRecord(-1, Fix64.Zero, startPolygon.Center);
            open.Push(startPolygon.Id, Heuristic(startPolygon.Center, endPolygon.Center));

            int visited = 0;
            while (open.Count > 0 && visited++ < maxVisited)
            {
                int currentId = open.Pop();
                if (currentId == endPolygon.Id)
                {
                    BuildPath(records, endPolygon.Id, start, end, result);
                    return true;
                }

                NavPolygon current;
                if (!_data.TryGetPolygon(currentId, out current))
                {
                    continue;
                }

                NodeRecord currentRecord = records[currentId];
                for (int i = 0; i < current.Neighbors.Count; i++)
                {
                    int neighborId = current.Neighbors[i];
                    NavPolygon neighbor;
                    if (!_data.TryGetPolygon(neighborId, out neighbor) || _blockedPolygons.Contains(neighborId))
                    {
                        continue;
                    }

                    Fix64 nextCost = currentRecord.CostFromStart + Heuristic(current.Center, neighbor.Center);
                    NodeRecord known;
                    if (records.TryGetValue(neighborId, out known) && known.CostFromStart <= nextCost)
                    {
                        continue;
                    }

                    records[neighborId] = new NodeRecord(currentId, nextCost, neighbor.Center);
                    Fix64 priority = nextCost + Heuristic(neighbor.Center, endPolygon.Center);
                    open.Push(neighborId, priority);
                }
            }

            return false;
        }

        private void RefreshBlockedPolygons()
        {
            if (_blockedVersion == _dynamicObstacles.Version)
            {
                return;
            }

            _blockedPolygons.Clear();
            foreach (NavObstacle obstacle in _dynamicObstacles.Obstacles)
            {
                for (int i = 0; i < _data.Polygons.Count; i++)
                {
                    NavPolygon polygon = _data.Polygons[i];
                    if (obstacle.Intersects(polygon.Bounds))
                    {
                        _blockedPolygons.Add(polygon.Id);
                    }
                }
            }

            _blockedVersion = _dynamicObstacles.Version;
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

        private static Fix64 Heuristic(FixedVector2 a, FixedVector2 b)
        {
            return FixedMath.Abs(a.X - b.X) + FixedMath.Abs(a.Y - b.Y);
        }

        private static void BuildPath(Dictionary<int, NodeRecord> records, int endId, FixedVector2 start, FixedVector2 end, List<FixedVector2> result)
        {
            var polygonIds = new List<int>();
            int id = endId;
            while (id != -1)
            {
                polygonIds.Add(id);
                id = records[id].PreviousId;
            }

            polygonIds.Reverse();
            result.Add(start);
            for (int i = 1; i < polygonIds.Count - 1; i++)
            {
                result.Add(records[polygonIds[i]].Center);
            }

            result.Add(end);
        }

        private readonly struct NodeRecord
        {
            public int PreviousId { get; }
            public Fix64 CostFromStart { get; }
            public FixedVector2 Center { get; }

            public NodeRecord(int previousId, Fix64 costFromStart, FixedVector2 center)
            {
                PreviousId = previousId;
                CostFromStart = costFromStart;
                Center = center;
            }
        }
    }
}
