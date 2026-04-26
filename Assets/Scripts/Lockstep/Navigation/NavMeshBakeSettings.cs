using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class NavMeshBakeSettings
    {
        public FixedVector2 Min { get; set; }
        public FixedVector2 Max { get; set; }
        public Fix64 CellSize { get; set; } = Fix64.One;
        public Fix64 AgentRadius { get; set; } = Fix64.Half;
        public List<NavObstacle> StaticObstacles { get; set; } = new List<NavObstacle>();
    }
}
