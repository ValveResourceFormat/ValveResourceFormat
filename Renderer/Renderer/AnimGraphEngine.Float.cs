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

        public override float GetValue(GraphContext ctx)
        {
            var input = InputValueNode.GetValue(ctx);
            return Operation switch
            {
                FloatAngleMathNode__Operation.ClampTo180 => ClampAngle180(input),
                FloatAngleMathNode__Operation.ClampTo360 => ClampAngle360(input),
                FloatAngleMathNode__Operation.FlipHemisphere => ClampAngle180(input - 180.0f),
                FloatAngleMathNode__Operation.FlipHemisphereNegate => -ClampAngle180(input - 180.0f),
                _ => throw new NotImplementedException()
            };
        }

        private static float ClampAngle180(float angle)
        {
            angle %= 360.0f;
            if (angle > 180.0f)
            {
                angle -= 360.0f;
            }

            if (angle <= -180.0f)
            {
                angle += 360.0f;
            }

            return angle;
        }

        private static float ClampAngle360(float angle)
        {
            angle %= 360.0f;
            if (angle < 0.0f)
            {
                angle += 360.0f;
            }

            return angle;
        }
    }

    partial class FloatClampNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx)
        {
            var input = InputValueNode.GetValue(ctx);
            return ClampRange.GetClampedValue(input);
        }
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

        public override float GetValue(GraphContext ctx)
        {
            var input = InputValueNode.GetValue(ctx);
            return Curve.Evaluate(input);
        }
    }

    partial class FloatEaseNode
    {
        FloatValueNode InputValueNode;
        float _easeBegin;
        float _easeEnd;
        float _currentValue;
        float _currentEaseTime;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
            if (UseStartValue)
            {
                _easeBegin = _easeEnd = StartValue;
            }
            else
            {
                _easeBegin = _easeEnd = InputValueNode.GetValue(ctx);
            }
            _currentValue = _easeBegin;
            _currentEaseTime = 0;
        }

        public override float GetValue(GraphContext ctx)
        {
            float inputTargetValue = InputValueNode.GetValue(ctx);
            if (Math.Abs(_currentValue - inputTargetValue) < 0.01f)
            {
                _easeBegin = _easeEnd = inputTargetValue;
                _currentValue = inputTargetValue;
                _currentEaseTime = 0;
            }
            else
            {
                if (inputTargetValue != _easeEnd)
                {
                    _easeEnd = inputTargetValue;
                    _easeBegin = _currentValue;
                    _currentEaseTime = 0;
                }
                _currentEaseTime += ctx.DeltaTime;
                float T = Math.Clamp(_currentEaseTime / EaseTime, 0.0f, 1.0f);
                float blendValue = T * (_easeEnd - _easeBegin);
                _currentValue = _easeBegin + blendValue;
            }
            return _currentValue;
        }
    }

    partial class FloatMathNode
    {
        FloatValueNode InputValueNodeA;
        FloatValueNode? InputValueNodeB;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdxA, ref InputValueNodeA);
            ctx.SetOptionalNodeFromIndex(InputValueNodeIdxB, ref InputValueNodeB);
        }

        public override float GetValue(GraphContext ctx)
        {
            float valueA = InputValueNodeA.GetValue(ctx);
            float valueB = InputValueNodeB?.GetValue(ctx) ?? ValueB;
            float result = Operator switch
            {
                FloatMathNode__Operator.Add => valueA + valueB,
                FloatMathNode__Operator.Sub => valueA - valueB,
                FloatMathNode__Operator.Mul => valueA * valueB,
                FloatMathNode__Operator.Div => valueB == 0 ? 0 : valueA / valueB,
                _ => throw new NotImplementedException()
            };
            if (ReturnAbsoluteResult) result = Math.Abs(result);
            if (ReturnNegatedResult) result = -result;
            return result;
        }
    }

    partial class FloatRemapNode
    {
        FloatValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override float GetValue(GraphContext ctx)
        {
            float input = InputValueNode.GetValue(ctx);
            return MathUtils.RemapRange(input, InputRange.m_begin, InputRange.m_end, OutputRange.m_begin, OutputRange.m_end);
        }
    }

    partial class FloatSelectorNode
    {
        FloatValueNode[] ConditionNodes;
        float _easeBegin;
        float _easeEnd;
        float _currentValue;
        float _currentEaseTime;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        public override float GetValue(GraphContext ctx)
        {
            float inputTargetValue = DefaultValue;
            for (int i = 0; i < ConditionNodes.Length; i++)
            {
                if (ConditionNodes[i].GetValue(ctx) != 0)
                {
                    inputTargetValue = Values[i];
                    break;
                }
            }
            if (EasingOp == EasingOperation.None)
            {
                _currentValue = inputTargetValue;
            }
            else
            {
                if (Math.Abs(_currentValue - inputTargetValue) < 0.01f)
                {
                    _easeBegin = _easeEnd = inputTargetValue;
                    _currentValue = inputTargetValue;
                    _currentEaseTime = 0;
                }
                else
                {
                    if (inputTargetValue != _easeEnd)
                    {
                        _easeEnd = inputTargetValue;
                        _easeBegin = _currentValue;
                        _currentEaseTime = 0;
                    }
                    _currentEaseTime += ctx.DeltaTime;
                    float T = Math.Clamp(_currentEaseTime / EaseTime, 0.0f, 1.0f);
                    float blendValue = T * (_easeEnd - _easeBegin);
                    _currentValue = _easeBegin + blendValue;
                }
            }
            return _currentValue;
        }
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

        public override float GetValue(GraphContext ctx)
        {
            bool switchValue = SwitchValueNode.GetValue(ctx) != 0;
            return switchValue ? TrueValueNode.GetValue(ctx) : FalseValueNode.GetValue(ctx);
        }
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
