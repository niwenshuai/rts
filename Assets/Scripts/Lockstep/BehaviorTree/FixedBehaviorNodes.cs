using System;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.BehaviorTree
{
    public enum BehaviorComparison
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    public sealed class BlackboardBoolConditionNode : BehaviorNode
    {
        private readonly string _key;
        private readonly bool _expectedValue;

        public BlackboardBoolConditionNode(string key, bool expectedValue = true)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _expectedValue = expectedValue;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            return context.Blackboard.TryGetBool(_key, out bool value) && value == _expectedValue
                ? BehaviorStatus.Success
                : BehaviorStatus.Failure;
        }
    }

    public sealed class BlackboardFix64ConditionNode : BehaviorNode
    {
        private readonly string _key;
        private readonly Fix64 _expectedValue;
        private readonly BehaviorComparison _comparison;

        public BlackboardFix64ConditionNode(string key, BehaviorComparison comparison, Fix64 expectedValue)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _comparison = comparison;
            _expectedValue = expectedValue;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (!context.Blackboard.TryGetFix64(_key, out Fix64 value))
            {
                return BehaviorStatus.Failure;
            }

            return Compare(value, _expectedValue, _comparison) ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }

        private static bool Compare(Fix64 value, Fix64 expectedValue, BehaviorComparison comparison)
        {
            switch (comparison)
            {
                case BehaviorComparison.Equal:
                    return value == expectedValue;
                case BehaviorComparison.NotEqual:
                    return value != expectedValue;
                case BehaviorComparison.Less:
                    return value < expectedValue;
                case BehaviorComparison.LessOrEqual:
                    return value <= expectedValue;
                case BehaviorComparison.Greater:
                    return value > expectedValue;
                case BehaviorComparison.GreaterOrEqual:
                    return value >= expectedValue;
                default:
                    return false;
            }
        }
    }

    public sealed class FixedDistanceConditionNode : BehaviorNode
    {
        private readonly string _fromKey;
        private readonly string _toKey;
        private readonly Fix64 _maxDistanceSqr;

        public FixedDistanceConditionNode(string fromKey, string toKey, Fix64 maxDistance)
        {
            _fromKey = fromKey ?? throw new ArgumentNullException(nameof(fromKey));
            _toKey = toKey ?? throw new ArgumentNullException(nameof(toKey));
            _maxDistanceSqr = maxDistance * maxDistance;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (!context.Blackboard.TryGetFixedVector3(_fromKey, out FixedVector3 from) ||
                !context.Blackboard.TryGetFixedVector3(_toKey, out FixedVector3 to))
            {
                return BehaviorStatus.Failure;
            }

            return (to - from).SqrMagnitude <= _maxDistanceSqr ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }

    public sealed class SendCommandNode : BehaviorNode
    {
        private readonly int _commandType;
        private readonly int _targetId;
        private readonly string _targetIdKey;
        private readonly FixedVector3 _position;
        private readonly string _positionKey;
        private readonly byte[] _payload;

        public SendCommandNode(int commandType, int targetId, FixedVector3 position, byte[] payload = null)
        {
            _commandType = commandType;
            _targetId = targetId;
            _position = position;
            _payload = payload ?? Array.Empty<byte>();
        }

        public SendCommandNode(int commandType, int targetId, string positionKey, byte[] payload = null)
        {
            _commandType = commandType;
            _targetId = targetId;
            _positionKey = positionKey ?? throw new ArgumentNullException(nameof(positionKey));
            _payload = payload ?? Array.Empty<byte>();
        }

        public SendCommandNode(int commandType, string targetIdKey, string positionKey, byte[] payload = null)
        {
            _commandType = commandType;
            _targetIdKey = targetIdKey ?? throw new ArgumentNullException(nameof(targetIdKey));
            _positionKey = positionKey ?? throw new ArgumentNullException(nameof(positionKey));
            _payload = payload ?? Array.Empty<byte>();
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (context.CommandSink == null)
            {
                return BehaviorStatus.Failure;
            }

            int targetId = _targetId;
            if (_targetIdKey != null && !context.Blackboard.TryGetInt(_targetIdKey, out targetId))
            {
                return BehaviorStatus.Failure;
            }

            FixedVector3 position = _position;
            if (_positionKey != null && !context.Blackboard.TryGetFixedVector3(_positionKey, out position))
            {
                return BehaviorStatus.Failure;
            }

            var command = new BehaviorCommand(_commandType, targetId, position, _payload);
            return context.CommandSink.TryEnqueue(context, command) ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }
}
