using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class TargetValueNode
    {
        public virtual Target GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class CachedTargetNode
    {
        TargetValueNode InputValueNode;
        Target CachedValue;
        bool HasCachedValue;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override Target GetValue(GraphContext ctx)
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
        public override Target GetValue(GraphContext ctx) => Value;
    }

    partial class ControlParameterTargetNode
    {
        //
    }

    partial class TargetOffsetNode
    {
        TargetValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override Target GetValue(GraphContext ctx)
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
        //
    }
}
