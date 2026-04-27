using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.Navigation
{
    internal sealed class BinaryHeap
    {
        private readonly List<Entry> _items = new List<Entry>();

        public int Count => _items.Count;

        public void Push(int polygonId, Fix64 priority)
        {
            _items.Add(new Entry(polygonId, priority));
            SiftUp(_items.Count - 1);
        }

        public int Pop()
        {
            Entry root = _items[0];
            Entry last = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            if (_items.Count > 0)
            {
                _items[0] = last;
                SiftDown(0);
            }

            return root.PolygonId;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (HasHigherPriority(_items[parent], _items[index]))
                {
                    return;
                }

                Entry temp = _items[parent];
                _items[parent] = _items[index];
                _items[index] = temp;
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < _items.Count && HasHigherPriority(_items[left], _items[smallest]))
                {
                    smallest = left;
                }

                if (right < _items.Count && HasHigherPriority(_items[right], _items[smallest]))
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    return;
                }

                Entry temp = _items[index];
                _items[index] = _items[smallest];
                _items[smallest] = temp;
                index = smallest;
            }
        }

        private static bool HasHigherPriority(Entry a, Entry b)
        {
            if (a.Priority != b.Priority)
            {
                return a.Priority < b.Priority;
            }

            return a.PolygonId <= b.PolygonId;
        }

        private readonly struct Entry
        {
            public int PolygonId { get; }
            public Fix64 Priority { get; }

            public Entry(int polygonId, Fix64 priority)
            {
                PolygonId = polygonId;
                Priority = priority;
            }
        }
    }
}
