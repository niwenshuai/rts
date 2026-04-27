using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public readonly struct NavMeshQueryFilter
    {
        public Fix64 AgentRadius { get; }
        public int IgnoreObstacleId { get; }
        public bool UseDynamicObstacles { get; }

        public NavMeshQueryFilter(Fix64 agentRadius, int ignoreObstacleId = 0, bool useDynamicObstacles = true)
        {
            AgentRadius = agentRadius.RawValue < 0 ? Fix64.Zero : agentRadius;
            IgnoreObstacleId = ignoreObstacleId;
            UseDynamicObstacles = useDynamicObstacles;
        }

        public static NavMeshQueryFilter Default => new NavMeshQueryFilter(Fix64.Zero);
    }
}
