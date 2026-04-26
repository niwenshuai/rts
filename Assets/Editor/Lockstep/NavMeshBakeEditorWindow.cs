using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AIRTS.Lockstep.Math;
using AIRTS.Lockstep.Navigation;
using UnityEditor;
using UnityEngine;

namespace AIRTS.Lockstep.UnityEditor
{
    public sealed class NavMeshBakeEditorWindow : EditorWindow
    {
        private GameObject _meshRoot;
        private Mesh _meshAsset;
        private bool _includeInactiveChildren = true;
        private float _cellSize = 1f;
        private float _agentRadius = 0.5f;
        private int _obstacleCount;
        private readonly List<GameObject> _obstacleRoots = new List<GameObject>();
        private NavMeshData _lastBake;
        private string _lastOutputPath = "Assets/navmesh.json";

        [MenuItem("AIRTS/NavMesh Baker")]
        public static void Open()
        {
            GetWindow<NavMeshBakeEditorWindow>("AIRTS NavMesh Baker");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _meshRoot = (GameObject)EditorGUILayout.ObjectField("Mesh Root", _meshRoot, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(_meshRoot != null))
            {
                _meshAsset = (Mesh)EditorGUILayout.ObjectField("Mesh Asset", _meshAsset, typeof(Mesh), false);
            }

            _includeInactiveChildren = EditorGUILayout.Toggle("Include Inactive Children", _includeInactiveChildren);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);
            _cellSize = EditorGUILayout.FloatField("Cell Size", Mathf.Max(0.01f, _cellSize));
            _agentRadius = EditorGUILayout.FloatField("Agent Radius", Mathf.Max(0f, _agentRadius));

            EditorGUILayout.Space(8f);
            DrawObstacles();

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (GUILayout.Button("Bake And Save Json", GUILayout.Height(32f)))
                {
                    BakeAndSave();
                }
            }

            if (_lastBake != null)
            {
                EditorGUILayout.HelpBox("Last bake polygons: " + _lastBake.Polygons.Count + "\nOutput: " + _lastOutputPath, MessageType.Info);
            }
        }

        private void DrawObstacles()
        {
            EditorGUILayout.LabelField("Static Obstacles", EditorStyles.boldLabel);
            _obstacleCount = Mathf.Max(0, EditorGUILayout.IntField("Count", _obstacleCount));
            while (_obstacleRoots.Count < _obstacleCount)
            {
                _obstacleRoots.Add(null);
            }

            while (_obstacleRoots.Count > _obstacleCount)
            {
                _obstacleRoots.RemoveAt(_obstacleRoots.Count - 1);
            }

            for (int i = 0; i < _obstacleRoots.Count; i++)
            {
                _obstacleRoots[i] = (GameObject)EditorGUILayout.ObjectField("Obstacle " + (i + 1), _obstacleRoots[i], typeof(GameObject), true);
            }
        }

        private bool CanBake()
        {
            return _cellSize > 0f && (_meshRoot != null || _meshAsset != null);
        }

        private void BakeAndSave()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save AIRTS NavMesh",
                "navmesh",
                "json",
                "Choose where to save the baked AIRTS NavMesh json.",
                Path.GetDirectoryName(_lastOutputPath));

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                NavMeshData data = UnityMeshNavMeshBaker.Bake(
                    _meshRoot,
                    _meshAsset,
                    _includeInactiveChildren,
                    _cellSize,
                    _agentRadius,
                    _obstacleRoots);

                File.WriteAllText(path, NavMeshEditorJsonWriter.ToJson(data));
                AssetDatabase.ImportAsset(path);
                _lastBake = data;
                _lastOutputPath = path;
                Debug.Log("AIRTS NavMesh baked: " + data.Polygons.Count + " polygons -> " + path);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("AIRTS NavMesh Bake Failed", ex.Message, "OK");
            }
        }
    }

    internal static class UnityMeshNavMeshBaker
    {
        public static NavMeshData Bake(
            GameObject meshRoot,
            Mesh meshAsset,
            bool includeInactiveChildren,
            float cellSize,
            float agentRadius,
            IEnumerable<GameObject> obstacleRoots)
        {
            var triangles = CollectTriangles(meshRoot, meshAsset, includeInactiveChildren);
            if (triangles.Count == 0)
            {
                throw new InvalidOperationException("No readable mesh triangles found.");
            }

            Bounds worldBounds = BuildBounds(triangles);
            int width = Mathf.Max(1, Mathf.CeilToInt(worldBounds.size.x / cellSize));
            int height = Mathf.Max(1, Mathf.CeilToInt(worldBounds.size.z / cellSize));
            var ids = new int[width, height];
            var boundsById = new List<FixedBounds2>(width * height);
            var obstacles = CollectObstacleBounds(obstacleRoots, agentRadius);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float minX = worldBounds.min.x + x * cellSize;
                    float minZ = worldBounds.min.z + y * cellSize;
                    Vector2 center = new Vector2(minX + cellSize * 0.5f, minZ + cellSize * 0.5f);
                    FixedBounds2 cellBounds = ToFixedBounds(minX, minZ, minX + cellSize, minZ + cellSize);

                    bool walkable = IsPointInsideAnyTriangle(center, triangles) && !IsBlocked(cellBounds, obstacles);
                    if (!walkable)
                    {
                        ids[x, y] = -1;
                        continue;
                    }

                    ids[x, y] = boundsById.Count;
                    boundsById.Add(cellBounds);
                }
            }

            var polygons = new List<NavPolygon>(boundsById.Count);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = ids[x, y];
                    if (id < 0)
                    {
                        continue;
                    }

                    var neighbors = new List<int>(4);
                    AddNeighbor(ids, x - 1, y, neighbors);
                    AddNeighbor(ids, x + 1, y, neighbors);
                    AddNeighbor(ids, x, y - 1, neighbors);
                    AddNeighbor(ids, x, y + 1, neighbors);
                    polygons.Add(new NavPolygon(id, boundsById[id], neighbors));
                }
            }

            return new NavMeshData(polygons);
        }

        private static List<ProjectedTriangle> CollectTriangles(GameObject meshRoot, Mesh meshAsset, bool includeInactiveChildren)
        {
            var triangles = new List<ProjectedTriangle>();
            if (meshRoot != null)
            {
                MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>(includeInactiveChildren);
                for (int i = 0; i < filters.Length; i++)
                {
                    Mesh mesh = filters[i].sharedMesh;
                    if (mesh != null)
                    {
                        AddMeshTriangles(mesh, filters[i].transform.localToWorldMatrix, triangles);
                    }
                }
            }
            else if (meshAsset != null)
            {
                AddMeshTriangles(meshAsset, Matrix4x4.identity, triangles);
            }

            return triangles;
        }

        private static void AddMeshTriangles(Mesh mesh, Matrix4x4 localToWorld, List<ProjectedTriangle> output)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = localToWorld.MultiplyPoint3x4(vertices[indices[i]]);
                Vector3 b = localToWorld.MultiplyPoint3x4(vertices[indices[i + 1]]);
                Vector3 c = localToWorld.MultiplyPoint3x4(vertices[indices[i + 2]]);
                output.Add(new ProjectedTriangle(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z)));
            }
        }

        private static Bounds BuildBounds(List<ProjectedTriangle> triangles)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, 0f, float.NegativeInfinity);
            for (int i = 0; i < triangles.Count; i++)
            {
                triangles[i].Encapsulate(ref min, ref max);
            }

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        private static List<NavObstacle> CollectObstacleBounds(IEnumerable<GameObject> obstacleRoots, float agentRadius)
        {
            var obstacles = new List<NavObstacle>();
            if (obstacleRoots == null)
            {
                return obstacles;
            }

            int id = 1;
            foreach (GameObject root in obstacleRoots)
            {
                if (root == null)
                {
                    continue;
                }

                if (TryGetWorldBounds(root, out Bounds bounds))
                {
                    FixedVector2 min = new FixedVector2(Fix64.FromFloat(bounds.min.x - agentRadius), Fix64.FromFloat(bounds.min.z - agentRadius));
                    FixedVector2 max = new FixedVector2(Fix64.FromFloat(bounds.max.x + agentRadius), Fix64.FromFloat(bounds.max.z + agentRadius));
                    obstacles.Add(new NavObstacle(id++, min, max));
                }
            }

            return obstacles;
        }

        private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = colliders[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
            }

            return hasBounds;
        }

        private static bool IsPointInsideAnyTriangle(Vector2 point, List<ProjectedTriangle> triangles)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                if (triangles[i].Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBlocked(FixedBounds2 cellBounds, List<NavObstacle> obstacles)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].Intersects(cellBounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static FixedBounds2 ToFixedBounds(float minX, float minZ, float maxX, float maxZ)
        {
            return new FixedBounds2(
                new FixedVector2(Fix64.FromFloat(minX), Fix64.FromFloat(minZ)),
                new FixedVector2(Fix64.FromFloat(maxX), Fix64.FromFloat(maxZ)));
        }

        private static void AddNeighbor(int[,] ids, int x, int y, List<int> neighbors)
        {
            if (x < 0 || y < 0 || x >= ids.GetLength(0) || y >= ids.GetLength(1))
            {
                return;
            }

            int id = ids[x, y];
            if (id >= 0)
            {
                neighbors.Add(id);
            }
        }

        private readonly struct ProjectedTriangle
        {
            private readonly Vector2 _a;
            private readonly Vector2 _b;
            private readonly Vector2 _c;

            public ProjectedTriangle(Vector2 a, Vector2 b, Vector2 c)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            public bool Contains(Vector2 p)
            {
                float d1 = Sign(p, _a, _b);
                float d2 = Sign(p, _b, _c);
                float d3 = Sign(p, _c, _a);
                bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
                bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
                return !(hasNegative && hasPositive);
            }

            public void Encapsulate(ref Vector3 min, ref Vector3 max)
            {
                EncapsulatePoint(_a, ref min, ref max);
                EncapsulatePoint(_b, ref min, ref max);
                EncapsulatePoint(_c, ref min, ref max);
            }

            private static void EncapsulatePoint(Vector2 point, ref Vector3 min, ref Vector3 max)
            {
                min.x = Mathf.Min(min.x, point.x);
                min.z = Mathf.Min(min.z, point.y);
                max.x = Mathf.Max(max.x, point.x);
                max.z = Mathf.Max(max.z, point.y);
            }

            private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            {
                return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
            }
        }
    }

    internal static class NavMeshEditorJsonWriter
    {
        public static string ToJson(NavMeshData data)
        {
            var builder = new StringBuilder(1024 + data.Polygons.Count * 96);
            builder.AppendLine("{");
            builder.AppendLine("  \"polygons\": [");
            for (int i = 0; i < data.Polygons.Count; i++)
            {
                NavPolygon polygon = data.Polygons[i];
                builder.AppendLine("    {");
                builder.Append("      \"id\": ").Append(polygon.Id).AppendLine(",");
                AppendVector(builder, "min", polygon.Bounds.Min, true);
                AppendVector(builder, "max", polygon.Bounds.Max, true);
                builder.Append("      \"neighbors\": [");
                for (int n = 0; n < polygon.Neighbors.Count; n++)
                {
                    if (n > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(polygon.Neighbors[n]);
                }

                builder.AppendLine("]");
                builder.Append("    }");
                if (i < data.Polygons.Count - 1)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendVector(StringBuilder builder, string name, FixedVector2 value, bool comma)
        {
            builder
                .Append("      \"")
                .Append(name)
                .Append("\": { \"x\": ")
                .Append(value.X.ToString())
                .Append(", \"y\": ")
                .Append(value.Y.ToString())
                .Append(" }");

            if (comma)
            {
                builder.Append(",");
            }

            builder.AppendLine();
        }
    }
}
