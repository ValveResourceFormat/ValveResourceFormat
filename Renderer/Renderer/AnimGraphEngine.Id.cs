using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class IDValueNode { public virtual GlobalSymbol GetValue(GraphContext ctx) => throw new NotImplementedException(); }

    partial class CachedIDNode
    {
        IDValueNode InputValueNode;
        GlobalSymbol CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override GlobalSymbol GetValue(GraphContext ctx)
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

    partial class ConstIDNode
    {
        public override GlobalSymbol GetValue(GraphContext ctx) => Value;
    }

    partial class ControlParameterIDNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override GlobalSymbol GetValue(GraphContext ctx)
        {
            return new GlobalSymbol(ctx.Controller.IdParameters[parameterName]);
        }
    }

    // A virtual parameter is a graph-computed sub-expression: evaluates its child (cached once per update).
    partial class VirtualParameterIDNode
    {
        IDValueNode ChildNode;
        GlobalSymbol cachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        public override GlobalSymbol GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = ChildNode.GetValue(ctx);
            }

            return cachedValue;
        }
    }
}
