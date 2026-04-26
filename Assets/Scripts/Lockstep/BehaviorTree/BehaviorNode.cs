using System;

namespace AIRTS.Lockstep.BehaviorTree
{
    public sealed class BehaviorTree
    {
        public BehaviorNode Root { get; }
        public BehaviorStatus LastStatus { get; private set; }

        public BehaviorTree(BehaviorNode root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            LastStatus = BehaviorStatus.Failure;
        }

        public BehaviorStatus Tick(BehaviorTreeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            LastStatus = Root.Tick(context);
            return LastStatus;
        }

        public void Reset()
        {
            Root.Reset();
            LastStatus = BehaviorStatus.Failure;
        }
    }

    public abstract class BehaviorNode
    {
        private bool _isStarted;

        public BehaviorStatus LastStatus { get; private set; }
        public bool IsRunning => _isStarted;

        public BehaviorStatus Tick(BehaviorTreeContext context)
        {
            if (!_isStarted)
            {
                _isStarted = true;
                OnStart(context);
            }

            BehaviorStatus status = OnTick(context);
            LastStatus = status;
            if (status != BehaviorStatus.Running)
            {
                OnStop(context, status);
                _isStarted = false;
            }

            return status;
        }

        public void Reset()
        {
            _isStarted = false;
            LastStatus = BehaviorStatus.Failure;
            OnReset();
        }

        protected virtual void OnStart(BehaviorTreeContext context)
        {
        }

        protected abstract BehaviorStatus OnTick(BehaviorTreeContext context);

        protected virtual void OnStop(BehaviorTreeContext context, BehaviorStatus status)
        {
        }

        protected virtual void OnReset()
        {
        }
    }
}
