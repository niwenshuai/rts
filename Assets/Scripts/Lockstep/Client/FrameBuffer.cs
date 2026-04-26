using System.Collections.Generic;
using AIRTS.Lockstep.Shared;

namespace AIRTS.Lockstep.Client
{
    public sealed class FrameBuffer
    {
        private readonly SortedDictionary<int, LockstepFrame> _frames = new SortedDictionary<int, LockstepFrame>();

        public int Count
        {
            get
            {
                lock (_frames)
                {
                    return _frames.Count;
                }
            }
        }

        public void Add(LockstepFrame frame)
        {
            lock (_frames)
            {
                _frames[frame.FrameIndex] = frame;
            }
        }

        public bool TryGet(int frameIndex, out LockstepFrame frame)
        {
            lock (_frames)
            {
                return _frames.TryGetValue(frameIndex, out frame);
            }
        }

        public bool TryPop(int frameIndex, out LockstepFrame frame)
        {
            lock (_frames)
            {
                if (_frames.TryGetValue(frameIndex, out frame))
                {
                    _frames.Remove(frameIndex);
                    return true;
                }

                return false;
            }
        }

        public void ClearBefore(int frameIndex)
        {
            lock (_frames)
            {
                var removeList = new List<int>();
                foreach (int key in _frames.Keys)
                {
                    if (key >= frameIndex)
                    {
                        break;
                    }

                    removeList.Add(key);
                }

                for (int i = 0; i < removeList.Count; i++)
                {
                    _frames.Remove(removeList[i]);
                }
            }
        }
    }
}
