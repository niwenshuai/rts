using System;
using System.Collections.Generic;

namespace AIRTS.Lockstep.BehaviorTree
{
    public abstract class CompositeNode : BehaviorNode
    {
        protected readonly List<BehaviorNode> Children;

        protected CompositeNode(params BehaviorNode[] children)
        {
            Children = new List<BehaviorNode>(children ?? Array.Empty<BehaviorNode>());
        }

        public CompositeNode AddChild(BehaviorNode child)
        {
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            Children.Add(child);
            return this;
        }

        protected override void OnStop(BehaviorTreeContext context, BehaviorStatus status)
        {
            ResetChildren();
        }

        protected override void OnReset()
        {
            ResetChildren();
        }

        protected void ResetChildren()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Reset();
            }
        }
    }

    public sealed class SequenceNode : CompositeNode
    {
        private int _currentIndex;

        public SequenceNode(params BehaviorNode[] children)
            : base(children)
        {
        }

        protected override void OnStart(BehaviorTreeContext context)
        {
            _currentIndex = 0;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            while (_currentIndex < Children.Count)
            {
                BehaviorStatus status = Children[_currentIndex].Tick(context);
                if (status != BehaviorStatus.Success)
                {
                    return status;
                }

                _currentIndex++;
            }

            return BehaviorStatus.Success;
        }
    }

    public sealed class SelectorNode : CompositeNode
    {
        private int _currentIndex;

        public SelectorNode(params BehaviorNode[] children)
            : base(children)
        {
        }

        protected override void OnStart(BehaviorTreeContext context)
        {
            _currentIndex = 0;
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            while (_currentIndex < Children.Count)
            {
                BehaviorStatus status = Children[_currentIndex].Tick(context);
                if (status != BehaviorStatus.Failure)
                {
                    return status;
                }

                _currentIndex++;
            }

            return BehaviorStatus.Failure;
        }
    }

    public sealed class ParallelNode : CompositeNode
    {
        private readonly int _successThreshold;
        private readonly int _failureThreshold;
        private bool[] _finished;
        private BehaviorStatus[] _statuses;

        public ParallelNode(int successThreshold, int failureThreshold, params BehaviorNode[] children)
            : base(children)
        {
            _successThreshold = System.Math.Max(1, successThreshold);
            _failureThreshold = System.Math.Max(1, failureThreshold);
        }

        protected override void OnStart(BehaviorTreeContext context)
        {
            _finished = new bool[Children.Count];
            _statuses = new BehaviorStatus[Children.Count];
        }

        protected override BehaviorStatus OnTick(BehaviorTreeContext context)
        {
            if (Children.Count == 0)
            {
                return BehaviorStatus.Success;
            }

            int successCount = 0;
            int failureCount = 0;
            int runningCount = 0;

            for (int i = 0; i < Children.Count; i++)
            {
                if (!_finished[i])
                {
                    _statuses[i] = Children[i].Tick(context);
                    if (_statuses[i] != BehaviorStatus.Running)
                    {
                        _finished[i] = true;
                    }
                }

                if (_statuses[i] == BehaviorStatus.Success)
                {
                    successCount++;
                }
                else if (_statuses[i] == BehaviorStatus.Failure)
                {
                    failureCount++;
                }
                else
                {
                    runningCount++;
                }
            }

            if (successCount >= _successThreshold)
            {
                return BehaviorStatus.Success;
            }

            if (failureCount >= _failureThreshold)
            {
                return BehaviorStatus.Failure;
            }

            return runningCount > 0 ? BehaviorStatus.Running : BehaviorStatus.Failure;
        }

        protected override void OnReset()
        {
            _finished = null;
            _statuses = null;
            base.OnReset();
        }
    }
}
