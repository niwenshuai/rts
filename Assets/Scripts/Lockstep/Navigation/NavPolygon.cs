using System.Collections.Generic;
using System.Linq;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    public sealed class NavPolygon
    {
        public int Id { get; }
        public FixedBounds2 Bounds { get; }
        public FixedVector2 Center { get; }
        public IReadOnlyList<int> Neighbors => _neighbors;

        private readonly int[] _neighbors;

        public NavPolygon(int id, FixedBounds2 bounds, IEnumerable<int> neighbors)
        {
            Id = id;
            Bounds = bounds;
            Center = bounds.Center;
            _neighbors = neighbors.ToArray();
        }
    }
}
