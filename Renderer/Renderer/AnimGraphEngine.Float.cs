using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class FloatValueNode
    {
        float cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public float GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual float GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return 0f;
        }
    }

    partial class CachedFloatNode
    {
        FloatValueNode InputValueNode;
        float CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
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
        protected override float GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterFloatNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            return ctx.Graph.FloatParameters[parameterName];
        }
    }

    // Returns the source state's current position on its sync track, as index and/or percentage.
    partial class CurrentSyncEventNode
    {
        StateNode SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var syncTrack = SourceStateNode.SyncTrack;
            var currentSyncTime = syncTrack.GetTime(SourceStateNode.CurrentTime);

            return InfoType switch
            {
                CurrentSyncEventNode__InfoType.IndexAndPercentage => currentSyncTime.EventIdx + currentSyncTime.PercentageThrough.Value,
                CurrentSyncEventNode__InfoType.IndexOnly => currentSyncTime.EventIdx,
                CurrentSyncEventNode__InfoType.PercentageOnly => currentSyncTime.PercentageThrough.Value,
                _ => throw new NotImplementedException(),
            };
        }
    }

    partial class FloatAngleMathNode
    {
        FloatValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var input = InputValueNode.GetValue(ctx);
            return ClampRange.GetClampedValue(input);
        }
    }

    // Evaluates the best matching sampled float-curve event, falling back to the default value
    // (Esoterica FloatCurveEventNode). Curve evaluation itself is not ported: CS2 float-curve
    // events are not surfaced as typed clip events yet, so a matched event keeps the default.
    partial class FloatCurveEventNode
    {
        FloatValueNode? DefaultNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(DefaultNodeIdx, ref DefaultNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var bestValueFound = DefaultNode?.GetValue(ctx) ?? DefaultValue;

            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var preferHigherWeight = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.PreferHighestWeight);

            var foundPercentageThrough = 0f;
            var highestWeightFound = -1f;
            var eventFound = false;

            // Searches the whole buffer (no source state restriction upstream)
            for (var i = 0; i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                if (sampledEvent.AnimEvent is not { ClassName: "CNmFloatCurveEvent" } curveEvent)
                {
                    continue;
                }

                // Filter based on event ID
                if (EventID.IsValid && new GlobalSymbol(curveEvent.Data.GetStringProperty("m_ID", string.Empty)) != EventID)
                {
                    continue;
                }

                var updateEvent = !eventFound
                    || (preferHigherWeight
                        ? sampledEvent.Weight >= highestWeightFound
                        : sampledEvent.PercentageThrough >= foundPercentageThrough);

                if (updateEvent)
                {
                    eventFound = true;
                    foundPercentageThrough = sampledEvent.PercentageThrough;
                    highestWeightFound = sampledEvent.Weight;
                    ctx.LogNodeNotImplemented(NodeIdx, "FloatCurveEventNode curve evaluation");
                }
            }

            return bestValueFound;
        }
    }

    partial class FloatCurveNode
    {
        FloatValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
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

        public override void Initialize(GraphContext ctx)
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

        protected override float GetValueInternal(GraphContext ctx)
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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdxA, ref InputValueNodeA);
            ctx.SetOptionalNodeFromIndex(InputValueNodeIdxB, ref InputValueNodeB);
        }

        protected override float GetValueInternal(GraphContext ctx)
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
                FloatMathNode__Operator.FractionalPart => a - MathF.Truncate(a),
                FloatMathNode__Operator.InverseFractionalPart => 1f - (a - MathF.Truncate(a)),

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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        protected override float GetValueInternal(GraphContext ctx)
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

    partial class FloatSpringNode
    {
        FloatValueNode InputValueNode;
        float CurrentValue;
        float CurrentVelocity;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
            CurrentValue = UseStartValue ? StartValue : InputValueNode.GetValue(ctx);
            CurrentVelocity = 0f;
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var target = InputValueNode.GetValue(ctx);
            var omega = 2f * MathF.PI * Hertz;
            var dt = ctx.DeltaTime;

            // Semi-implicit Euler spring integration
            // Implicit Euler, matching Esoterica (stable at high stiffness / large steps)
            var omegaDt = omega * dt;
            var scale = 1f / (1f + omegaDt * (2f * DampingRatio + omegaDt));
            CurrentVelocity = scale * (CurrentVelocity - omegaDt * omega * (CurrentValue - target));
            CurrentValue += dt * CurrentVelocity;

            return CurrentValue;
        }
    }

    partial class FloatSwitchNode
    {
        BoolValueNode SwitchValueNode;
        FloatValueNode? TrueValueNode;
        FloatValueNode? FalseValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);

            // The value inputs are optional; unbound inputs use the inline constants instead.
            ctx.SetOptionalNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetOptionalNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var switchValue = SwitchValueNode.GetValue(ctx);
            return switchValue
                ? TrueValueNode?.GetValue(ctx) ?? TrueValue
                : FalseValueNode?.GetValue(ctx) ?? FalseValue;
        }
    }

    // Percentage through the best matching sampled foot event, or -1 when none matched the phase
    // condition (Esoterica FootstepEventPercentageThroughNode).
    partial class FootstepEventPercentageThroughNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var preferHigherWeight = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.PreferHighestWeight);

            var foundPercentageThrough = 0f;
            var highestWeightFound = -1f;
            var eventFound = false;

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                if (!EventSearch.TryGetFootPhase(sampledEvent, out var foot))
                {
                    continue;
                }

                // Filter by the phase condition (the phase groups include None here, matching C++)
                var passesFilter = PhaseCondition switch
                {
                    FootPhaseCondition.LeftFootDown => foot == FootPhase.LeftFootDown,
                    FootPhaseCondition.RightFootDown => foot == FootPhase.RightFootDown,
                    FootPhaseCondition.LeftFootPassing => foot == FootPhase.LeftFootPassing,
                    FootPhaseCondition.RightFootPassing => foot == FootPhase.RightFootPassing,
                    FootPhaseCondition.LeftPhase => foot is FootPhase.None or FootPhase.RightFootDown or FootPhase.LeftFootPassing,
                    FootPhaseCondition.RightPhase => foot is FootPhase.None or FootPhase.LeftFootDown or FootPhase.RightFootPassing,
                    FootPhaseCondition.None => foot == FootPhase.None,
                    _ => false,
                };

                if (!passesFilter)
                {
                    continue;
                }

                var updateEvent = !eventFound
                    || (preferHigherWeight
                        ? sampledEvent.Weight >= highestWeightFound
                        : sampledEvent.PercentageThrough >= foundPercentageThrough);

                if (updateEvent)
                {
                    eventFound = true;
                    foundPercentageThrough = sampledEvent.PercentageThrough;
                    highestWeightFound = sampledEvent.Weight;
                }
            }

            return eventFound ? foundPercentageThrough : -1f;
        }
    }

    // Percentage through the best matching sampled ID animation event with the requested ID, or -1
    // (Esoterica IDEventPercentageThroughNode).
    partial class IDEventPercentageThroughNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var foundPercentageThrough = 0f;
            var highestWeightFound = -1f;
            var eventFound = false;

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var preferHigherWeight = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.PreferHighestWeight);

            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                if (sampledEvent.AnimEvent is not ValveResourceFormat.ResourceTypes.ModelAnimation2.NmIDEvent || EventID != sampledEvent.ID)
                {
                    continue;
                }

                var updateEvent = !eventFound
                    || (preferHigherWeight
                        ? sampledEvent.Weight >= highestWeightFound
                        : sampledEvent.PercentageThrough >= foundPercentageThrough);

                if (updateEvent)
                {
                    eventFound = true;
                    foundPercentageThrough = sampledEvent.PercentageThrough;
                    highestWeightFound = sampledEvent.Weight;
                }
            }

            return eventFound ? foundPercentageThrough : -1f;
        }
    }

    partial class IDToFloatNode
    {
        IDValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
            Debug.Assert(IDs.Length == Values.Length);
        }

        protected override float GetValueInternal(GraphContext ctx)
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

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref TargetNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
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
                        var degrees = float.RadiansToDegrees(angle);

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
                        return float.RadiansToDegrees((MathF.PI / 2f) - MathF.Acos(dotUp));
                    }

                case TargetInfoNode__Info.Distance:
                    return inputTargetTransform.Position.Length();

                case TargetInfoNode__Info.DistanceHorizontalOnly:
                    return inputTargetTransform.Position.AsVector2().Length();

                case TargetInfoNode__Info.DistanceVerticalOnly:
                    return MathF.Abs(inputTargetTransform.Position.Z);

                case TargetInfoNode__Info.DeltaOrientationX:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Angle);
                        return e.X;
                    }

                case TargetInfoNode__Info.DeltaOrientationY:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Angle);
                        return e.Y;
                    }

                case TargetInfoNode__Info.DeltaOrientationZ:
                    {
                        var e = EntityTransformHelper.ToEulerAngles(inputTargetTransform.Angle);
                        return e.Z;
                    }

                default:
                    return 0.0f;
            }
        }
    }

    partial class VirtualParameterFloatNode
    {
        FloatValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the FloatValueNode base.
        protected override float GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
