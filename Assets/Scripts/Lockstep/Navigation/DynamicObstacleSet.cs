using System.Collections.Generic;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class DynamicObstacleSet
    {
        private readonly Dictionary<int, NavObstacle> _obstacles = new Dictionary<int, NavObstacle>();

        public int Version { get; private set; }
        public IEnumerable<NavObstacle> Obstacles => _obstacles.Values;

        public void Upsert(NavObstacle obstacle)
        {
            _obstacles[obstacle.Id] = obstacle;
            Version++;
        }

        public bool Remove(int id)
        {
            if (_obstacles.Remove(id))
            {
                Version++;
                return true;
            }

            return false;
        }
    }
}
