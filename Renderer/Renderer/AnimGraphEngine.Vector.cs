using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class VectorValueNode { public virtual Vector3 GetValue(GraphContext ctx) => throw new NotImplementedException(); }

    partial class CachedVectorNode
    {
        VectorValueNode InputValueNode;
        Vector3 CachedValue;
        bool HasCachedValue;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override Vector3 GetValue(GraphContext ctx)
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

    partial class ConstVectorNode
    {
        public override Vector3 GetValue(GraphContext ctx) => Value;
    }

    partial class ControlParameterVectorNode
    {
        string parameterName;

        public void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override Vector3 GetValue(GraphContext ctx)
        {
            return ctx.Controller.VectorParameters[parameterName].AsVector3();
        }
    }
}
