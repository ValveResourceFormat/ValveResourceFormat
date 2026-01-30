using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class IDValueNode { public virtual GlobalSymbol Evaluate(GraphContext ctx) => throw new NotImplementedException(); }

    partial class CachedIDNode
    {
        IDValueNode InputValueNode;
        GlobalSymbol CachedValue;
        bool HasCachedValue;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override GlobalSymbol Evaluate(GraphContext ctx)
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
                    CachedValue = InputValueNode.Evaluate(ctx);
                }
            }

            return CachedValue;
        }
    }

    partial class ConstIDNode
    {
        public override GlobalSymbol Evaluate(GraphContext ctx) => Value;
    }

    partial class ControlParameterIDNode
    {
        string parameterName;

        public void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override GlobalSymbol Evaluate(GraphContext ctx)
        {
            return new GlobalSymbol(ctx.Controller.IdParameters[parameterName]);
        }
    }
}
