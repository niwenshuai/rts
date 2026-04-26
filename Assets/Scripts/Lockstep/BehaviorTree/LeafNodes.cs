using System;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.BehaviorTree
{
    public sealed class ConditionNode : BehaviorNode
    {
        private readonly Func<BehaviorTreeContext, bool> _predicate;

        public ConditionNode(Func<BehaviorTreeContext, bool> predicate)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            return _predicate(context) ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }

    public sealed class ActionNode : BehaviorNode
    {
        private readonly Func<BehaviorTreeContext, BehaviorStatus> _action;

        public ActionNode(Func<BehaviorTreeContext, BehaviorStatus> action)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            return _action(context);
        }
    }

    public sealed class WaitNode : BehaviorNode
    {
        private readonly Fix64 _duration;
        private Fix64 _elapsed;

        public WaitNode(Fix64 duration)
        {
            _duration = duration.RawValue < 0 ? Fix64.Zero : duration;
        }

        protected override void OnStart(BehaviorTreeContext context)
        {
            _elapsed = Fix64.Zero;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            _elapsed += context.DeltaTime;
            return _elapsed >= _duration ? BehaviorStatus.Success : BehaviorStatus.Running;
        }
    }

    public sealed class SetBlackboardNode : BehaviorNode
    {
        private readonly string _key;
        private readonly BehaviorValue _value;

        public SetBlackboardNode(string key, BehaviorValue value)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _value = value;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            context.Blackboard.Set(_key, _value);
            return BehaviorStatus.Success;
        }
    }

    public sealed class HasBlackboardValueNode : BehaviorNode
    {
        private readonly string _key;

        public HasBlackboardValueNode(string key)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            return context.Blackboard.Contains(_key) ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }
}
