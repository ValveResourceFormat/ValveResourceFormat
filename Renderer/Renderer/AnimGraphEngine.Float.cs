using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class FloatValueNode { public virtual float GetValue(GraphContext ctx) => throw new NotImplementedException(); }

    partial class CachedFloatNode
    {
        FloatValueNode InputValueNode;
        float CachedValue;
        bool HasCachedValue;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx)
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

    partial class ConstFloatNode
    {
        public override float GetValue(GraphContext ctx) => Value;
    }

    partial class ControlParameterFloatNode
    {
        string parameterName;

        public void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override float GetValue(GraphContext ctx)
        {
            return ctx.Controller.FloatParameters[parameterName];
        }
    }
}
