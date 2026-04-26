using System;
using AIRTS.Lockstep.Math;

namespace AIRTS.Lockstep.BehaviorTree
{
    public abstract class DecoratorNode : BehaviorNode
    {
        protected BehaviorNode Child { get; }

        protected DecoratorNode(BehaviorNode child)
        {
            Child = child ?? throw new ArgumentNullException(nameof(child));
        }

        protected override void OnStop(BehaviorTreeContext context, BehaviorStatus status)
        {
            Child.Reset();
        }

        protected override void OnReset()
        {
            Child.Reset();
        }
    }

    public sealed class InverterNode : DecoratorNode
    {
        public InverterNode(BehaviorNode child)
            : base(child)
        {
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            BehaviorStatus status = Child.Tick(context);
            if (status == BehaviorStatus.Success)
            {
                return BehaviorStatus.Failure;
            }

            return status == BehaviorStatus.Failure ? BehaviorStatus.Success : BehaviorStatus.Running;
        }
    }

    public sealed class SucceederNode : DecoratorNode
    {
        public SucceederNode(BehaviorNode child)
            : base(child)
        {
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            BehaviorStatus status = Child.Tick(context);
            return status == BehaviorStatus.Running ? BehaviorStatus.Running : BehaviorStatus.Success;
        }
    }

    public sealed class RepeatNode : DecoratorNode
    {
        private readonly int _repeatCount;
        private int _completedCount;

        public RepeatNode(BehaviorNode child, int repeatCount = -1)
            : base(child)
        {
            _repeatCount = repeatCount;
        }

        protected override void OnStart(BehaviorTreeContext context)
        {
            _completedCount = 0;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (_repeatCount >= 0 && _completedCount >= _repeatCount)
            {
                return BehaviorStatus.Success;
            }

            BehaviorStatus status = Child.Tick(context);
            if (status != BehaviorStatus.Success)
            {
                return status;
            }

            _completedCount++;
            Child.Reset();
            return _repeatCount >= 0 && _completedCount >= _repeatCount
                ? BehaviorStatus.Success
                : BehaviorStatus.Running;
        }
    }

    public sealed class CooldownNode : DecoratorNode
    {
        private readonly Fix64 _duration;
        private readonly bool _startOnSuccessOnly;
        private Fix64 _nextAllowedTime;

        public CooldownNode(BehaviorNode child, Fix64 duration, bool startOnSuccessOnly = true)
            : base(child)
        {
            _duration = duration.RawValue < 0 ? Fix64.Zero : duration;
            _startOnSuccessOnly = startOnSuccessOnly;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (context.Time < _nextAllowedTime)
            {
                return BehaviorStatus.Failure;
            }

            BehaviorStatus status = Child.Tick(context);
            if (status != BehaviorStatus.Running && (!_startOnSuccessOnly || status == BehaviorStatus.Success))
            {
                _nextAllowedTime = context.Time + _duration;
            }

            return status;
        }

        protected override void OnReset()
        {
            _nextAllowedTime = Fix64.Zero;
            base.OnReset();
        }
    }
}
