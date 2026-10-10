using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Plays a referenced child graph instance within this graph's layer and branch, reflecting this
    // graph's same-named parameters into the child's control parameters.
    partial class ReferencedGraphNode
    {
        AnimationGraph? childGraph;
        PoseNode? FallbackNode;

        // Child control parameter name and the parent parameter node that drives it
        (string Name, ValueNode Source)[] parameterMapping = [];

        // For a child authored on another skeleton: the child bone for each parent bone by name, -1 if it has none
        int[]? boneMap;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(FallbackNodeIdx, ref FallbackNode);

            childGraph = ctx.GetReferencedGraph(ReferencedGraphIdx);

            if (childGraph == null)
            {
                return;
            }

            List<(string, ValueNode)> mapping = [];
            var childNodes = childGraph.Context.Nodes;

            for (var childParamIdx = 0; childParamIdx < childGraph.ParameterNames.Length; childParamIdx++)
            {
                var childParamName = childGraph.ParameterNames[childParamIdx];
                var parentParameterNode = ctx.GetParameterNode(childParamName);
                if (parentParameterNode == null)
                {
                    continue;
                }

                if (GetValueType(parentParameterNode) != GetValueType(childNodes[childParamIdx]))
                {
                    ctx.LogWarning(NodeIdx, $"Mismatch parameter type for referenced graph parameter '{childParamName}'");
                    continue;
                }

                mapping.Add((childParamName, parentParameterNode));
            }

            parameterMapping = [.. mapping];

            // Shared graphs can fall back to a default variation authored on another character's skeleton
            if (!string.Equals(childGraph.SkeletonName, ctx.Graph.SkeletonName, StringComparison.OrdinalIgnoreCase))
            {
                boneMap = BuildBoneMap(ctx.Graph.Skeleton, childGraph.Skeleton);
            }
        }

        static int[] BuildBoneMap(ValveResourceFormat.ResourceTypes.ModelAnimation.Skeleton parent, ValveResourceFormat.ResourceTypes.ModelAnimation.Skeleton child)
        {
            Dictionary<string, int> childBones = new(child.Bones.Length, StringComparer.OrdinalIgnoreCase);

            foreach (var bone in child.Bones)
            {
                childBones.TryAdd(bone.Name, bone.Index);
            }

            var map = new int[parent.Bones.Length];

            for (var i = 0; i < map.Length; i++)
            {
                map[i] = childBones.GetValueOrDefault(parent.Bones[i].Name, -1);
            }

            return map;
        }

        static Type? GetValueType(GraphNode node) => node switch
        {
            BoolValueNode => typeof(BoolValueNode),
            IDValueNode => typeof(IDValueNode),
            FloatValueNode => typeof(FloatValueNode),
            VectorValueNode => typeof(VectorValueNode),
            TargetValueNode => typeof(TargetValueNode),
            _ => null,
        };

        public override bool IsValid => childGraph != null || (FallbackNode?.IsValid ?? false);

        public override SyncTrack SyncTrack
        {
            get
            {
                if (childGraph != null)
                {
                    var childRoot = childGraph.Context.RootNode;
                    return childRoot.IsValid ? childRoot.SyncTrack : SyncTrack.Default;
                }

                return FallbackNode is { IsValid: true } ? FallbackNode.SyncTrack : SyncTrack.Default;
            }
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            if (childGraph != null)
            {
                ReflectControlParametersFromParent(ctx);

                // Reset the referenced instance at the initial time
                childGraph.Context.ResetReferencedGraphState(ctx, initialTime);

                var childRoot = childGraph.Context.RootNode;
                Debug.Assert(childRoot.IsInitialized);
                PreviousTime = childRoot.CurrentTime;
                CurrentTime = childRoot.CurrentTime;
                Duration = childRoot.Duration;
            }
            else
            {
                PreviousTime = CurrentTime = 0f;
                Duration = 0f;

                // Initialize the fallback node if set
                if (FallbackNode != null)
                {
                    FallbackNode.Initialize(ctx, initialTime);
                    Duration = FallbackNode.Duration;
                    PreviousTime = FallbackNode.PreviousTime;
                    CurrentTime = FallbackNode.CurrentTime;
                }
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            // The referenced instance itself stays initialized (Esoterica leaves it alive until the
            // owning instance is destroyed); only the fallback participates.
            if (childGraph == null && FallbackNode != null)
            {
                FallbackNode.Shutdown(ctx);
            }

            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (childGraph == null)
            {
                if (FallbackNode is { IsValid: true })
                {
                    var fallbackResult = FallbackNode.Update(ctx, updateRange);
                    Duration = FallbackNode.Duration;
                    PreviousTime = FallbackNode.PreviousTime;
                    CurrentTime = FallbackNode.CurrentTime;
                    return fallbackResult;
                }

                // A slot filled at runtime, empty in the compiled graph
                var emptyResult = base.Update(ctx);
                emptyResult.NoPose = true;
                return emptyResult;
            }

            ReflectControlParametersFromParent(ctx);

            var eventRangeStart = ctx.SampledEvents.Count;
            var timing = ctx.UpdateDetails?.BeginTiming(childGraph.Name, ctx.GraphDepth + 1) ?? -1;
            var childResult = childGraph.Context.EvaluateReferencedGraph(ctx, updateRange);

            if (timing >= 0)
            {
                ctx.UpdateDetails!.EndTiming(timing);
            }
            ForwardParameterHintsToParent();

            // Surface the child's events so parent conditions can see them
            ctx.SampledEvents.AppendFrom(childGraph.Context.SampledEvents);

            var result = base.Update(ctx);

            if (boneMap == null)
            {
                var count = Math.Min(childResult.Pose.Length, result.Pose.Length);
                childResult.Pose.AsSpan(0, count).CopyTo(result.Pose);
            }
            else
            {
                // Bones the child skeleton lacks keep the pose that leaves them unchanged
                var defaultPose = ctx.GetDefaultPose();
                var count = Math.Min(boneMap.Length, result.Pose.Length);

                for (var i = 0; i < count; i++)
                {
                    var childBone = boneMap[i];
                    result.Pose[i] = childBone >= 0 && childBone < childResult.Pose.Length ? childResult.Pose[childBone] : defaultPose[i];
                }
            }
            result.RootMotionDelta = childResult.RootMotionDelta;
            result.SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);
            result.NoPose = childResult.NoPose;

            var childRoot = childGraph.Context.RootNode;
            Duration = childRoot.Duration;
            PreviousTime = childRoot.CurrentTime;
            CurrentTime = childRoot.CurrentTime;

            return result;
        }

        private void ReflectControlParametersFromParent(GraphContext ctx)
        {
            Debug.Assert(childGraph != null);

            foreach (var (name, source) in parameterMapping)
            {
                switch (source)
                {
                    case BoolValueNode boolNode:
                        childGraph.BoolParameters[name] = boolNode.GetValue(ctx);
                        break;
                    case IDValueNode idNode:
                        var id = idNode.GetValue(ctx);
                        childGraph.IdParameters[name] = id.IsValid ? id.Name : string.Empty;
                        break;
                    case FloatValueNode floatNode:
                        childGraph.FloatParameters[name] = floatNode.GetValue(ctx);
                        break;
                    case VectorValueNode vectorNode:
                        childGraph.VectorParameters[name] = new Vector4(vectorNode.GetValue(ctx), 0f);
                        break;
                    case TargetValueNode targetNode:
                        var target = targetNode.GetValue(ctx);
                        childGraph.TargetParameters[name] = target.IsSet ? target.Transform : null;
                        break;
                }
            }
        }

        // Hints reported inside the child graph surface on the parent parameters feeding it
        private void ForwardParameterHintsToParent()
        {
            Debug.Assert(childGraph != null);

            foreach (var (name, source) in parameterMapping)
            {
                if (!childGraph.ParameterHints.TryGetValue(name, out var hint))
                {
                    continue;
                }

                switch (source)
                {
                    case ControlParameterTargetNode targetParameter:
                        targetParameter.ReportHint(hint.Transform, hint.IsWorldSpace);
                        break;
                    case ControlParameterVectorNode vectorParameter:
                        vectorParameter.ReportHint(hint.Transform.Position, hint.IsWorldSpace);
                        break;
                }
            }
        }
    }
}
