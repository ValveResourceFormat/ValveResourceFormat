using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class VectorValueNode
    {
        Vector3 cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public Vector3 GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual Vector3 GetValueInternal(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class CachedVectorNode
    {
        VectorValueNode InputValueNode;
        Vector3 CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
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
        protected override Vector3 GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterVectorNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            return ctx.Controller.VectorParameters[parameterName].AsVector3();
        }
    }

    // A virtual parameter is a graph-computed sub-expression: evaluates its child (cached once per update).
    partial class VirtualParameterVectorNode
    {
        VectorValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the VectorValueNode base.
        protected override Vector3 GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }

    partial class TargetPointNode
    {
        TargetValueNode TargetNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref TargetNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            var target = TargetNode.GetValue(ctx);
            if (!target.IsSet || !target.TryGetTransform(ctx.Pose, out var transform))
            {
                return Vector3.Zero;
            }

            if (IsWorldSpaceTarget)
            {
                transform *= ctx.WorldTransformInverse;
            }

            return transform.Position;
        }
    }
}
