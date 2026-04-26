using System.Collections.Generic;
using System.Linq;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class NavMeshData
    {
        public IReadOnlyList<NavPolygon> Polygons { get; }

        private readonly Dictionary<int, NavPolygon> _byId;

        public NavMeshData(IEnumerable<NavPolygon> polygons)
        {
            Polygons = polygons.OrderBy(p => p.Id).ToArray();
            _byId = Polygons.ToDictionary(p => p.Id);
        }

        public bool TryGetPolygon(int id, out NavPolygon polygon)
        {
            return _byId.TryGetValue(id, out polygon);
        }
    }
}
