using System;
using System.Collections.Generic;
using System.IO;

namespace AIRTS.Lockstep.Shared
{
    [Serializable]
    public sealed class LockstepFrame
    {
        public int FrameIndex { get; }
        public IReadOnlyList<PlayerCommand> Commands => _commands;

        private readonly List<PlayerCommand> _commands;

        public LockstepFrame(int frameIndex, IEnumerable<PlayerCommand> commands)
        {
            FrameIndex = frameIndex;
            _commands = new List<PlayerCommand>(commands ?? Array.Empty<PlayerCommand>());
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(FrameIndex);
            writer.Write(_commands.Count);
            for (int i = 0; i < _commands.Count; i++)
            {
                _commands[i].Write(writer);
            }
        }

        public static LockstepFrame Read(BinaryReader reader)
        {
            int frameIndex = reader.ReadInt32();
            int count = reader.ReadInt32();
            var commands = new List<PlayerCommand>(count);
            for (int i = 0; i < count; i++)
            {
                commands.Add(PlayerCommand.Read(reader));
            }

            return new LockstepFrame(frameIndex, commands);
        }
    }
}
