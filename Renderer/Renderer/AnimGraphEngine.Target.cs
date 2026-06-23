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

        protected virtual Target GetValueInternal(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class CachedTargetNode
    {
        TargetValueNode InputValueNode;
        Target CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
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
        //
    }

    partial class TargetOffsetNode
    {
        TargetValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the TargetValueNode base.
        protected override Target GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
