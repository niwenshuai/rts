using System;
using System.Collections.Generic;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.BehaviorTree
{
    public sealed class BehaviorTreeContext
    {
        public int ActorId { get; }
        public int LogicFrame { get; private set; }
        public Fix64 DeltaTime { get; private set; }
        public Fix64 Time { get; private set; }
        public BehaviorBlackboard Blackboard { get; }
        public DeterministicRandom Random { get; }
        public IBehaviorCommandSink CommandSink { get; set; }

        public BehaviorTreeContext(
            int actorId,
            BehaviorBlackboard blackboard = null,
            IBehaviorCommandSink commandSink = null,
            uint randomSeed = 1)
        {
            ActorId = actorId;
            Blackboard = blackboard ?? new BehaviorBlackboard();
            CommandSink = commandSink;
            Random = new DeterministicRandom(randomSeed);
        }

        public void AdvanceFrame(int logicFrame, Fix64 deltaTime)
        {
            LogicFrame = logicFrame;
            DeltaTime = deltaTime;
            Time += deltaTime;
        }
    }

    public struct BehaviorCommand
    {
        public int CommandType;
        public int TargetId;
        public FixedVector3 Position;
        public byte[] Payload;

        public BehaviorCommand(int commandType, int targetId, FixedVector3 position, byte[] payload = null)
        {
            CommandType = commandType;
            TargetId = targetId;
            Position = position;
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    public interface IBehaviorCommandSink
    {
        bool TryEnqueue(BehaviorTreeContext context, BehaviorCommand command);
    }

    public sealed class BehaviorCommandQueue : IBehaviorCommandSink
    {
        private readonly List<BehaviorCommand> _commands = new List<BehaviorCommand>();

        public IReadOnlyList<BehaviorCommand> Commands => _commands;

        public void Clear()
        {
            _commands.Clear();
        }

        public bool TryEnqueue(BehaviorTreeContext context, BehaviorCommand command)
        {
            _commands.Add(command);
            return true;
        }
    }

    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0 ? 1u : seed;
        }

        public uint NextUInt()
        {
            _state = unchecked(_state * 1664525u + 1013904223u);
            return _state;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public Fix64 NextFix64()
        {
            long raw = NextUInt() % Fix64.Scale;
            return Fix64.FromRaw(raw);
        }
    }
}
