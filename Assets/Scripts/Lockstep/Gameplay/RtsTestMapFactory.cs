using System.Collections.Generic;
using AIRTS.Lockstep.Math;
using AIRTS.Lockstep.Navigation;

namespace AIRTS.Lockstep.Gameplay
{
    public static class RtsTestMapFactory
    {
        public static NavMeshData CreateEllipseNavMesh()
        {
            Fix64 radiusX = RtsGameplayConstants.MapRadiusX;
            Fix64 radiusZ = RtsGameplayConstants.MapRadiusZ;
            Fix64 cellSize = RtsGameplayConstants.MapCellSize;
            int width = (radiusX * Fix64.FromInt(2) / cellSize).ToInt();
            int height = (radiusZ * Fix64.FromInt(2) / cellSize).ToInt();
            var ids = new int[width, height];
            var boundsById = new List<FixedBounds2>(width * height);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Fix64 minX = -radiusX + Fix64.FromInt(x) * cellSize;
                    Fix64 minZ = -radiusZ + Fix64.FromInt(z) * cellSize;
                    Fix64 maxX = minX + cellSize;
                    Fix64 maxZ = minZ + cellSize;
                    FixedVector2 center = new FixedVector2((minX + maxX) / Fix64.FromInt(2), (minZ + maxZ) / Fix64.FromInt(2));

                    Fix64 normalized = center.X * center.X / (radiusX * radiusX) +
                        center.Y * center.Y / (radiusZ * radiusZ);
                    if (normalized > Fix64.One)
                    {
                        ids[x, z] = -1;
                        continue;
                    }

                    int id = boundsById.Count;
                    ids[x, z] = id;
                    boundsById.Add(new FixedBounds2(new FixedVector2(minX, minZ), new FixedVector2(maxX, maxZ)));
                }
            }

            var polygons = new List<NavPolygon>(boundsById.Count);
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = ids[x, z];
                    if (id < 0)
                    {
                        continue;
                    }

                    var neighbors = new List<int>(8);
                    AddNeighbor(ids, x - 1, z, neighbors);
                    AddNeighbor(ids, x + 1, z, neighbors);
                    AddNeighbor(ids, x, z - 1, neighbors);
                    AddNeighbor(ids, x, z + 1, neighbors);
                    AddDiagonalNeighbor(ids, x, z, -1, -1, neighbors);
                    AddDiagonalNeighbor(ids, x, z, -1, 1, neighbors);
                    AddDiagonalNeighbor(ids, x, z, 1, -1, neighbors);
                    AddDiagonalNeighbor(ids, x, z, 1, 1, neighbors);
                    polygons.Add(new NavPolygon(id, boundsById[id], neighbors));
                }
            }

            return new NavMeshData(polygons);
        }

        public static bool IsInsideEllipse(FixedVector2 point)
        {
            Fix64 radiusX = RtsGameplayConstants.MapRadiusX;
            Fix64 radiusZ = RtsGameplayConstants.MapRadiusZ;
            Fix64 normalized = point.X * point.X / (radiusX * radiusX) +
                point.Y * point.Y / (radiusZ * radiusZ);
            return normalized <= Fix64.One;
        }

        private static void AddNeighbor(int[,] ids, int x, int z, List<int> neighbors)
        {
            if (!IsWalkable(ids, x, z))
            {
                return;
            }

            neighbors.Add(ids[x, z]);
        }

        private static void AddDiagonalNeighbor(int[,] ids, int x, int z, int dx, int dz, List<int> neighbors)
        {
            if (!IsWalkable(ids, x + dx, z) || !IsWalkable(ids, x, z + dz))
            {
                return;
            }

            AddNeighbor(ids, x + dx, z + dz, neighbors);
        }

        private static bool IsWalkable(int[,] ids, int x, int z)
        {
            return x >= 0 &&
                z >= 0 &&
                x < ids.GetLength(0) &&
                z < ids.GetLength(1) &&
                ids[x, z] >= 0;
        }
    }
}
