using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Passes its child pose node through unchanged. Base for speed/duration scaling and (later)
    // root-motion override / warp / IK nodes.
    partial class PassthroughNode
    {
        public PoseNode? ChildNode;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        public override bool IsValid => ChildNode?.IsValid ?? false;

        public override SyncTrack SyncTrack => ChildNode?.SyncTrack ?? SyncTrack.Default;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            ChildNode?.Initialize(ctx, initialTime);

            if (ChildNode is { IsValid: true })
            {
                Duration = ChildNode.Duration;
                PreviousTime = ChildNode.PreviousTime;
                CurrentTime = ChildNode.CurrentTime;
            }
            else
            {
                PreviousTime = CurrentTime = 0f;
                Duration = 0f;
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ChildNode?.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (ChildNode is not { IsValid: true })
            {
                return base.Update(ctx);
            }

            var result = ChildNode.Update(ctx, updateRange);
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

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            InputValueNode?.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            InputValueNode?.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected virtual float CalculateSpeedScaleMultiplier(GraphContext ctx) => 1f;

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            // Speed scaling has no effect on a synchronized update's time step, but the scaled
            // duration is still reported so the parent's sync step accounts for it (Esoterica).
            if (updateRange != null)
            {
                var syncResult = base.Update(ctx, updateRange);

                if (ChildNode is { IsValid: true })
                {
                    var syncSpeedScale = CalculateSpeedScaleMultiplier(ctx);
                    Duration = syncSpeedScale < NearZero ? 0f : ChildNode.Duration / syncSpeedScale;
                }

                return syncResult;
            }

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
            if (childDuration > 0f)
            {
                // A zero desired duration would be an infinite speed; freeze instead of NaN/Inf.
                return desiredDuration > 1e-5f ? childDuration / desiredDuration : 0f;
            }

            return 1f;
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

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            // Select an option and initialize it; an invalid selection is shut down and discarded
            var selectedIndex = SelectOption(ctx);
            if (selectedIndex != -1)
            {
                SelectedNode = OptionNodes[selectedIndex];
                SelectedNode.Initialize(ctx, initialTime);

                if (SelectedNode.IsValid)
                {
                    Duration = SelectedNode.Duration;
                    PreviousTime = SelectedNode.PreviousTime;
                    CurrentTime = SelectedNode.CurrentTime;
                }
                else
                {
                    SelectedNode.Shutdown(ctx);
                    SelectedNode = null;
                }
            }

            if (SelectedNode == null)
            {
                ctx.LogWarning(NodeIdx, "Selector: Failed to select a valid option!");
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            if (SelectedNode != null)
            {
                SelectedNode.Shutdown(ctx);
                SelectedNode = null;
            }

            base.ShutdownInternal(ctx);
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

        public override SyncTrack SyncTrack => SelectedNode?.SyncTrack ?? SyncTrack.Default;

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (SelectedNode == null)
            {
                return base.Update(ctx);
            }

            var result = SelectedNode.Update(ctx, updateRange);
            Duration = SelectedNode.Duration;
            PreviousTime = SelectedNode.PreviousTime;
            CurrentTime = SelectedNode.CurrentTime;
            return result;
        }
    }

    // Valve extension: selects a pose option by matching an ID parameter against per-option IDs,
    // falling back to a dedicated fallback node when no option matches.
    partial class IDBasedSelectorNode
    {
        public PoseNode[] OptionNodes;
        public IDValueNode ParameterNode;
        public PoseNode? FallbackNode;
        public PoseNode? SelectedNode;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
            ctx.SetOptionalNodeFromIndex(FallbackNodeIdx, ref FallbackNode);
        }

        int SelectOption(GraphContext ctx)
        {
            var id = ParameterNode.GetValue(ctx);
            var optionCount = Math.Min(OptionIDs.Length, OptionNodes.Length);

            for (var i = 0; i < optionCount; i++)
            {
                if (OptionIDs[i] == id)
                {
                    if (IgnoreInvalidOptions && !OptionNodes[i].IsValid)
                    {
                        continue;
                    }

                    return i;
                }
            }

            return -1;
        }

        // Initializes the selection and validates it; on failure the fallback is retried
        // (Esoterica IDBasedSelectorNode::InitializeInternal).
        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            Debug.Assert(SelectedNode == null);
            base.InitializeInternal(ctx, initialTime);

            var selectedIndex = SelectOption(ctx);
            if (selectedIndex != -1)
            {
                SelectedNode = OptionNodes[selectedIndex];
            }
            else if (FallbackNode != null)
            {
                SelectedNode = FallbackNode;
            }

            bool TryInitializeSelectedNode()
            {
                SelectedNode!.Initialize(ctx, initialTime);

                if (SelectedNode.IsValid)
                {
                    Duration = SelectedNode.Duration;
                    PreviousTime = SelectedNode.PreviousTime;
                    CurrentTime = SelectedNode.CurrentTime;
                    return true;
                }

                SelectedNode.Shutdown(ctx);
                SelectedNode = null;
                return false;
            }

            if (SelectedNode != null)
            {
                if (!TryInitializeSelectedNode() && FallbackNode != null)
                {
                    SelectedNode = FallbackNode;
                    TryInitializeSelectedNode();
                }
            }

            if (SelectedNode is not { IsValid: true })
            {
                ctx.LogWarning(NodeIdx, "ID Selector: Failed to select a valid option!");
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            if (SelectedNode != null)
            {
                SelectedNode.Shutdown(ctx);
                SelectedNode = null;
            }

            base.ShutdownInternal(ctx);
        }

        public override bool IsValid => SelectedNode?.IsValid ?? false;

        public override SyncTrack SyncTrack => SelectedNode?.SyncTrack ?? SyncTrack.Default;

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (SelectedNode == null)
            {
                return base.Update(ctx);
            }

            var result = SelectedNode.Update(ctx, updateRange);
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

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            // Select an option and initialize it; an invalid selection is shut down and discarded
            var selectedIndex = SelectOption(ctx);
            if (selectedIndex != -1)
            {
                SelectedNode = OptionNodes[selectedIndex];
                SelectedNode.Initialize(ctx, initialTime);

                if (SelectedNode.IsValid)
                {
                    Duration = SelectedNode.Duration;
                    PreviousTime = SelectedNode.PreviousTime;
                    CurrentTime = SelectedNode.CurrentTime;
                }
                else
                {
                    SelectedNode.Shutdown(ctx);
                    SelectedNode = null;
                }
            }

            if (SelectedNode == null)
            {
                ctx.LogWarning(NodeIdx, "Parameterized Selector: Failed to select a valid option!");
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            if (SelectedNode != null)
            {
                SelectedNode.Shutdown(ctx);
                SelectedNode = null;
            }

            base.ShutdownInternal(ctx);
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
            // Zero-weight options exist in shipped data; they are simply never picked.
            Span<int> boundaries = stackalloc int[numOptions];
            var totalWeightedOptions = 0;
            for (var i = 0; i < numOptions; i++)
            {
                totalWeightedOptions += OptionWeights[i];
                boundaries[i] = totalWeightedOptions;
            }

            if (totalWeightedOptions == 0)
            {
                return -1;
            }

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

        public override SyncTrack SyncTrack => SelectedNode?.SyncTrack ?? SyncTrack.Default;

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (SelectedNode == null)
            {
                return base.Update(ctx);
            }

            var result = SelectedNode.Update(ctx, updateRange);
            Duration = SelectedNode.Duration;
            PreviousTime = SelectedNode.PreviousTime;
            CurrentTime = SelectedNode.CurrentTime;
            return result;
        }
    }
}
