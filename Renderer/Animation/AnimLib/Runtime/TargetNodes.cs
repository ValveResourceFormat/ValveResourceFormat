using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class TargetValueNode
    {
        Target cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public Target GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual Target GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return default;
        }
    }

    partial class CachedTargetNode
    {
        TargetValueNode InputValueNode;
        Target CachedValue;
        bool HasCachedValue;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);

            InputValueNode.Initialize(ctx);

            // Cache on entry
            if (Mode == CachedValueMode.OnEntry)
            {
                CachedValue = InputValueNode.GetValue(ctx);
                HasCachedValue = true;
            }
            else
            {
                HasCachedValue = false;
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            InputValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            if (!HasCachedValue)
            {
                Debug.Assert(Mode == CachedValueMode.OnExit);

                if (ctx.BranchState == BranchState.Inactive)
                {
                    HasCachedValue = true;
                }
                else
                {
                    CachedValue = InputValueNode.GetValue(ctx);
                }
            }

            return CachedValue;
        }
    }

    partial class ConstTargetNode
    {
        protected override Target GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterTargetNode
    {
        string parameterName;
        AnimationGraph owner;

        public override void Instantiate(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
            owner = ctx.Graph;
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            if (owner.BoneTargetParameters.TryGetValue(parameterName, out var boneTarget))
            {
                return boneTarget;
            }

            return owner.TargetParameters[parameterName] is { } transform ? new Target(transform) : default;
        }

        public void ReportHint(Transform transform, bool isWorldSpace)
            => owner.ParameterHints[parameterName] = new(transform, isWorldSpace);
    }

    partial class TargetOffsetNode
    {
        TargetValueNode InputValueNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            InputValueNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            InputValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            var target = InputValueNode.GetValue(ctx);

            if (target.IsSet)
            {
                if (target.IsBoneTarget)
                {
                    target.SetOffsets(RotationOffset, TranslationOffset, IsBoneSpaceOffset);
                }
                else
                {
                    ctx.LogWarning(NodeIdx, "Trying to set an offset on a transform target node - Offset are only allowed on bone targets!");
                }
            }
            else
            {
                ctx.LogWarning(NodeIdx, "Trying to set an offset on an unset node!");
            }

            return target;
        }
    }

    partial class VirtualParameterTargetNode : TargetValueNode
    {
        TargetValueNode ChildNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the TargetValueNode base.
        protected override Target GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
