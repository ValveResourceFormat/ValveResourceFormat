using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Passes its child pose node through unchanged. Base for speed/duration scaling and (later)
    // root-motion override / warp / IK nodes.
    partial class PassthroughNode
    {
        public PoseNode? ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetOptionalNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        public override bool IsValid => ChildNode?.IsValid ?? false;

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            if (ChildNode == null)
            {
                return base.Update(ctx);
            }

            var result = ChildNode.Update(ctx);
            Duration = ChildNode.Duration;
            PreviousTime = ChildNode.PreviousTime;
            CurrentTime = ChildNode.CurrentTime;
            return result;
        }
    }

    // Scales the playback speed of the child by adjusting the delta time. Unsynchronized only for now;
    // the synchronized (transition-driven) path is handled in the later sync-track refine pass.
    partial class SpeedScaleBaseNode
    {
        public FloatValueNode? InputValueNode;

        const float NearZero = 1e-5f;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetOptionalNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected virtual float CalculateSpeedScaleMultiplier(GraphContext ctx) => 1f;

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var speedScale = CalculateSpeedScaleMultiplier(ctx);
            Debug.Assert(speedScale >= 0f);

            var deltaTime = ctx.DeltaTime;

            var actualDuration = 0f;
            var childValid = ChildNode?.IsValid ?? false;
            if (childValid)
            {
                // Zero scale is equivalent to a single pose animation
                if (speedScale < NearZero)
                {
                    ctx.DeltaTime = 0f;
                    actualDuration = 0f;
                }
                else
                {
                    ctx.DeltaTime *= speedScale;
                    actualDuration = ChildNode!.Duration / speedScale;
                }
            }

            var result = base.Update(ctx);

            if (childValid)
            {
                Duration = actualDuration;
            }

            ctx.DeltaTime = deltaTime;
            return result;
        }
    }

    partial class SpeedScaleNode
    {
        protected override float CalculateSpeedScaleMultiplier(GraphContext ctx)
        {
            if (InputValueNode != null)
            {
                var multiplier = InputValueNode.GetValue(ctx);
                if (multiplier < 0f)
                {
                    ctx.LogWarning(NodeIdx, "Negative speed scale is not supported!");
                    multiplier = 0f;
                }

                return multiplier;
            }

            Debug.Assert(DefaultInputValue > 0f);
            return DefaultInputValue;
        }
    }

    partial class DurationScaleNode
    {
        protected override float CalculateSpeedScaleMultiplier(GraphContext ctx)
        {
            var desiredDuration = InputValueNode?.GetValue(ctx) ?? DefaultInputValue;
            if (desiredDuration < 0f)
            {
                ctx.LogWarning(NodeIdx, "Negative duration is not supported!");
                desiredDuration = 0f;
            }

            var childDuration = (ChildNode?.IsValid ?? false) ? ChildNode!.Duration : -1f;
            if (childDuration > 0f && desiredDuration > 0f)
            {
                return childDuration / desiredDuration;
            }

            // desiredDuration == 0 would be an infinite speed; freeze instead of producing NaN/Inf delta time.
            return desiredDuration <= 0f ? 0f : 1f;
        }
    }

    partial class VelocityBasedSpeedScaleNode
    {
        bool warned;

        protected override float CalculateSpeedScaleMultiplier(GraphContext ctx)
        {
            // TODO: requires the child clip's average linear velocity, which comes from decoded root
            // motion (not yet available — see ModelAnimation2.AnimationClip). Until then, fall back to
            // no scaling so the node is a passthrough rather than a hard failure.
            if (!warned)
            {
                ctx.LogWarning(NodeIdx, "VelocityBasedSpeedScale falling back to 1.0 (clip average velocity not yet available).");
                warned = true;
            }

            return 1f;
        }
    }

    // Selects one of N child pose nodes by the first satisfied boolean condition. Selection happens at
    // initialization (matching Esoterica), then the node passes the selected child through.
    partial class SelectorNode
    {
        public PoseNode[] OptionNodes;
        public BoolValueNode[] ConditionNodes;
        public PoseNode? SelectedNode;
        bool hasSelected;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);

            // Selection happens lazily on the first Update — condition value nodes may not be initialized
            // yet here (nodes initialize in array order). Selection is then fixed, matching Esoterica's
            // select-once-per-activation behaviour.
            hasSelected = false;
            SelectedNode = null;
        }

        void EnsureSelected(GraphContext ctx)
        {
            if (hasSelected)
            {
                return;
            }

            hasSelected = true;

            var selectedIndex = SelectOption(ctx);
            if (selectedIndex >= 0)
            {
                SelectedNode = OptionNodes[selectedIndex];
            }
            else
            {
                ctx.LogWarning(NodeIdx, "Failed to select a valid option!");
            }
        }

        int SelectOption(GraphContext ctx)
        {
            Debug.Assert(OptionNodes.Length == ConditionNodes.Length);
            for (var i = 0; i < ConditionNodes.Length; i++)
            {
                if (ConditionNodes[i].GetValue(ctx))
                {
                    return i;
                }
            }

            return -1;
        }

        public override bool IsValid => SelectedNode?.IsValid ?? false;

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            EnsureSelected(ctx);

            if (SelectedNode == null)
            {
                return base.Update(ctx);
            }

            var result = SelectedNode.Update(ctx);
            Duration = SelectedNode.Duration;
            PreviousTime = SelectedNode.PreviousTime;
            CurrentTime = SelectedNode.CurrentTime;
            return result;
        }
    }

    // Selects one of N child pose nodes using a numeric parameter as a seed (with optional weight buckets).
    partial class ParameterizedSelectorNode
    {
        public PoseNode[] OptionNodes;
        public FloatValueNode ParameterNode;
        public PoseNode? SelectedNode;
        bool hasSelected;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);

            // Selection happens lazily on the first Update (parameter node may not be initialized yet here).
            hasSelected = false;
            SelectedNode = null;
        }

        void EnsureSelected(GraphContext ctx)
        {
            if (hasSelected)
            {
                return;
            }

            hasSelected = true;

            var selectedIndex = SelectOption(ctx);
            if (selectedIndex >= 0)
            {
                SelectedNode = OptionNodes[selectedIndex];
            }
            else
            {
                ctx.LogWarning(NodeIdx, "Failed to select a valid option!");
            }
        }

        int SelectOption(GraphContext ctx)
        {
            var numOptions = OptionNodes.Length;
            if (numOptions == 0)
            {
                return -1;
            }

            var parameterValue = ParameterNode.GetValue(ctx);
            var seed = (int)Math.Floor(Math.Abs(parameterValue));

            if (!HasWeightsSet)
            {
                return seed % numOptions;
            }

            Debug.Assert(OptionWeights.Length == numOptions);

            // Build cumulative bucket boundaries from the byte weights (matches ParameterizedClipSelectorNode).
            Span<int> boundaries = stackalloc int[numOptions];
            var totalWeightedOptions = 0;
            for (var i = 0; i < numOptions; i++)
            {
                Debug.Assert(OptionWeights[i] > 0);
                totalWeightedOptions += OptionWeights[i];
                boundaries[i] = totalWeightedOptions;
            }

            Debug.Assert(totalWeightedOptions > 0);

            var weightedIdx = seed % totalWeightedOptions;
            for (var i = 0; i < numOptions; i++)
            {
                if (weightedIdx < boundaries[i])
                {
                    return i;
                }
            }

            return -1;
        }

        public override bool IsValid => SelectedNode?.IsValid ?? false;

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            EnsureSelected(ctx);

            if (SelectedNode == null)
            {
                return base.Update(ctx);
            }

            var result = SelectedNode.Update(ctx);
            Duration = SelectedNode.Duration;
            PreviousTime = SelectedNode.PreviousTime;
            CurrentTime = SelectedNode.CurrentTime;
            return result;
        }
    }
}
