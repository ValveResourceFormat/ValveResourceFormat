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

    partial class CurrentSyncEventNode
    {
        FloatValueNode SourceStateNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatAngleMathNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatClampNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatCurveEventNode
    {
        FloatValueNode DefaultNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(DefaultNodeIdx, ref DefaultNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatCurveNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatEaseNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatMathNode
    {
        FloatValueNode InputValueNodeA;
        FloatValueNode InputValueNodeB;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdxA, ref InputValueNodeA);
            ctx.SetNodeFromIndex(InputValueNodeIdxB, ref InputValueNodeB);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatRemapNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatSelectorNode
    {
        FloatValueNode[] ConditionNodes;

        public void Initialize(GraphContext ctx)
        {
            ConditionNodes = new FloatValueNode[ConditionNodeIndices.Length];
            for (int i = 0; i < ConditionNodeIndices.Length; i++)
            {
                ctx.SetNodeFromIndex(ConditionNodeIndices[i], ref ConditionNodes[i]);
            }
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FloatSwitchNode
    {
        FloatValueNode SwitchValueNode;
        FloatValueNode TrueValueNode;
        FloatValueNode FalseValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class FootstepEventPercentageThroughNode
    {
        FloatValueNode SourceStateNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class IDToFloatNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class TargetInfoNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class VectorInfoNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class VirtualParameterFloatNode
    {
        FloatValueNode ChildNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        public override float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }
}
