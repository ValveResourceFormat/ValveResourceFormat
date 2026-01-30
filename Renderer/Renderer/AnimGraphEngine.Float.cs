using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class FloatValueNode
    {
        public virtual float GetValue(GraphContext ctx) => throw new NotImplementedException();
    }

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
        Range EaseRange;
        float CurrentValue;
        float CurrentEaseTime;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
            if (UseStartValue)
            {
                EaseRange = new(StartValue);
            }
            else
            {
                EaseRange = new(InputValueNode.GetValue(ctx));
            }
            CurrentValue = EaseRange.Min;
            CurrentEaseTime = 0;
        }

        public override float GetValue(GraphContext ctx)
        {
            var inputTargetValue = InputValueNode.GetValue(ctx);
            PerformRangeEase(ctx.DeltaTime, inputTargetValue,
                ref EaseRange,
                ref CurrentValue,
                ref CurrentEaseTime,
                EaseTime, EasingOp);

            return CurrentValue;
        }

        public static void PerformRangeEase(float deltaTime, float inputTargetValue,
            ref Range Range,
            ref float CurrentValue,
            ref float CurrentEaseTime,
            float EaseTime,
            EasingOperation EasingOp)
        {
            if (Math.Abs(CurrentValue - inputTargetValue) < 0.01f)
            {
                Range = new(inputTargetValue);
                CurrentValue = inputTargetValue;
                CurrentEaseTime = 0;
                return;
            }

            if (inputTargetValue != Range.Max)
            {
                Range.Min = CurrentValue;
                Range.Max = inputTargetValue;
                CurrentEaseTime = 0;
            }

            CurrentEaseTime += deltaTime;
            var t = MathUtils.Saturate(CurrentEaseTime / EaseTime);
            var blendValue = MathUtils.Ease(EasingOp, t) * Range.Length;
            CurrentValue = Range.Min + blendValue;
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
            var a = InputValueNodeA.GetValue(ctx);
            var b = InputValueNodeB?.GetValue(ctx) ?? ValueB;

            var result = Operator switch
            {
                FloatMathNode__Operator.Add => a + b,
                FloatMathNode__Operator.Sub => a - b,
                FloatMathNode__Operator.Mul => a * b,
                FloatMathNode__Operator.Div => b == 0 ? 0 : a / b,

                // unary / other ops (ignore `b`)
                FloatMathNode__Operator.Mod => b == 0 ? 0 : a % b,
                FloatMathNode__Operator.Abs => MathF.Abs(a),
                FloatMathNode__Operator.Negate => -a,
                FloatMathNode__Operator.Floor => MathF.Floor(a),
                FloatMathNode__Operator.Ceiling => MathF.Ceiling(a),

                // integer / fractional decomposition uses floor so fractional part is in [0,1)
                FloatMathNode__Operator.IntegerPart => MathF.Floor(a),
                FloatMathNode__Operator.FractionalPart => a - MathF.Floor(a),
                FloatMathNode__Operator.InverseFractionalPart => 1f - (a - MathF.Floor(a)),

                _ => throw new UnreachableException()
            };

            if (ReturnAbsoluteResult)
            {
                result = Math.Abs(result);
            }

            if (ReturnNegatedResult)
            {
                result = -result;
            }

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
            var input = InputValueNode.GetValue(ctx);
            return MathUtils.RemapRange(input, InputRange.Begin, InputRange.End, OutputRange.Begin, OutputRange.End);
        }
    }

    partial class FloatSelectorNode
    {
        BoolValueNode[] ConditionNodes;
        Range EaseRange;
        float CurrentValue;
        float CurrentEaseTime;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        public override float GetValue(GraphContext ctx)
        {
            // Select value
            //-------------------------------------------------------------------------
            var inputTargetValue = DefaultValue;
            for (var i = 0; i < ConditionNodes.Length; i++)
            {
                if (ConditionNodes[i].GetValue(ctx))
                {
                    inputTargetValue = Values[i];
                    break;
                }
            }

            if (EasingOp == EasingOperation.None)
            {
                return inputTargetValue;
            }

            // Perform easing
            //-------------------------------------------------------------------------
            FloatEaseNode.PerformRangeEase(
                ctx.DeltaTime,
                inputTargetValue,
                ref EaseRange,
                ref CurrentValue,
                ref CurrentEaseTime,
                EaseTime, EasingOp);

            return CurrentValue;
        }
    }

    partial class FloatSwitchNode
    {
        BoolValueNode SwitchValueNode;
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
            var switchValue = SwitchValueNode.GetValue(ctx);
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
        IDValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
            Debug.Assert(IDs.Length == Values.Length);
        }

        public override float GetValue(GraphContext ctx)
        {
            var inputId = InputValueNode.GetValue(ctx);

            var foundIndex = IDs.IndexOf(inputId);
            if (foundIndex >= 0)
            {
                return Values[foundIndex];
            }

            return DefaultValue;
        }
    }

    partial class TargetInfoNode
    {
        TargetValueNode TargetNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref TargetNode);
        }

        public override float GetValue(GraphContext ctx)
        {
            var target = TargetNode.GetValue(ctx);

            if (!target.IsSet)
            {
                return 0.0f;
            }

            var isValidTransform = target.TryGetTransform(ctx.Pose, out var inputTargetTransform);

            // todo: this code seems to sometimes reuse last computed value, but we don't store it
            var lastValue = 1f;
            if (!isValidTransform)
            {
                return lastValue;
            }

            if (IsWorldSpaceTarget)
            {
                inputTargetTransform *= ctx.WorldTransformInverse;
            }

            switch (InfoType)
            {
                case TargetInfoNode__Info.AngleHorizontal:
                    {
                        var dir2 = inputTargetTransform.Position.AsVector2();
                        if (dir2.LengthSquared() < 1e-6f)
                        {
                            return 0.0f;
                        }

                        var dirN = Vector2.Normalize(dir2);
                        var dotForward = Math.Clamp(Vector2.Dot(dirN, Vector2.UnitX), -1f, 1f);
                        var angle = MathF.Acos(dotForward);
                        var degrees = MathUtils.ToDegrees(angle);

                        var dotRight = Vector2.Dot(dirN, Vector2.UnitY);
                        return dotRight < 0.0f ? -degrees : degrees;
                    }

                case TargetInfoNode__Info.AngleVertical:
                    {
                        var dir3 = inputTargetTransform.Position;
                        if (dir3.LengthSquared() < 1e-6f)
                        {
                            return 0.0f;
                        }

                        var dirN = Vector3.Normalize(dir3);
                        var dotUp = Math.Clamp(Vector3.Dot(Vector3.UnitZ, dirN), -1f, 1f);
                        return MathUtils.ToDegrees((MathF.PI / 2f) - MathF.Acos(dotUp));
                    }

                case TargetInfoNode__Info.Distance:
                    return inputTargetTransform.Position.Length();

                case TargetInfoNode__Info.DistanceHorizontalOnly:
                    return inputTargetTransform.Position.AsVector2().Length();

                case TargetInfoNode__Info.DistanceVerticalOnly:
                    return MathF.Abs(inputTargetTransform.Position.Z);

                case TargetInfoNode__Info.DeltaOrientationX:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Rotation);
                        return e.X;
                    }

                case TargetInfoNode__Info.DeltaOrientationY:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Rotation);
                        return e.Y;
                    }

                case TargetInfoNode__Info.DeltaOrientationZ:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Rotation);
                        return e.Z;
                    }

                default:
                    return 0.0f;
            }
        }
    }

    partial class VectorInfoNode
    {
        TargetValueNode TargetNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref TargetNode);
        }

        public override float GetValue(GraphContext ctx)
        {
            return 1f;
        }
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
