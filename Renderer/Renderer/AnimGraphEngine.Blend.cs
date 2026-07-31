using ValveResourceFormat.ResourceTypes.ModelAnimation;
using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Blends 2..N source pose nodes along a 1D parameter using a parameterization (set of blend ranges).
    // Unsynchronized: each source advances on its own time; sync-track alignment is a later refine pass.
    partial class ParameterizedBlendNode
    {
        public PoseNode[] SourceNodes;

        public override void Restart(GraphContext ctx)
        {
            base.Restart(ctx);

            if (SourceNodes == null)
            {
                return;
            }

            foreach (var sourceNode in SourceNodes)
            {
                sourceNode.Restart(ctx);
            }
        }
        public FloatValueNode InputParameterValueNode;

        protected ParameterizedBlendNode__Parameterization ActiveParameterization;

        PoseNode? blendSource0;
        PoseNode? blendSource1;
        float blendWeight;
        uint blendSpaceUpdateID = uint.MaxValue;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(SourceNodeIndices, ref SourceNodes);
            ctx.SetNodeFromIndex(InputParameterValueNodeIdx, ref InputParameterValueNode);
        }

        // Matches Esoterica: only the input parameter is required, sources may be individually invalid.
        public override bool IsValid => SourceNodes is { Length: > 1 } && InputParameterValueNode != null;

        SyncTrack? blendedSyncTrack;
        readonly SyncTrack ownedSyncTrack = SyncTrack.CreateBlendScratch();

        public override SyncTrack SyncTrack => blendedSyncTrack ?? SyncTrack.Default;

        protected void EvaluateBlendSpace(GraphContext ctx)
        {
            // Only evaluate the blend space once per graph update.
            if (blendSpaceUpdateID == ctx.UpdateID)
            {
                return;
            }

            blendSpaceUpdateID = ctx.UpdateID;

            blendSource0 = null;
            blendSource1 = null;
            blendWeight = 0f;

            var value = InputParameterValueNode.GetValue(ctx);
            value = ActiveParameterization.ParameterRange.GetClampedValue(value);

            foreach (var range in ActiveParameterization.BlendRanges)
            {
                if (range.ParameterValueRange.ContainsInclusive(value))
                {
                    var weight = range.ParameterValueRange.GetPercentageThrough(value);
                    if (weight <= 1e-4f)
                    {
                        blendSource0 = SourceNodes[range.InputIdx0];
                    }
                    else if (weight >= 1f - 1e-4f)
                    {
                        blendSource0 = SourceNodes[range.InputIdx1];
                    }
                    else
                    {
                        blendSource0 = SourceNodes[range.InputIdx0];
                        blendSource1 = SourceNodes[range.InputIdx1];
                        blendWeight = weight;
                    }

                    break;
                }
            }

            // Fallback for a degenerate parameterization: clamp to the first source.
            blendSource0 ??= SourceNodes.Length > 0 ? SourceNodes[0] : null;

            // Calculate the blended sync track and duration. Even single-source cases build a blended
            // track to strip any start event offsets (matching Esoterica).
            if (blendSource0 == null)
            {
                blendedSyncTrack = SyncTrack.Default;
                Duration = 0f;
            }
            else if (blendSource1 == null)
            {
                ownedSyncTrack.SetToBlendOf(blendSource0.SyncTrack, blendSource0.SyncTrack, 0f);
                blendedSyncTrack = ownedSyncTrack;
                Duration = blendSource0.Duration;
            }
            else
            {
                var syncTrack0 = blendSource0.SyncTrack;
                var syncTrack1 = blendSource1.SyncTrack;
                ownedSyncTrack.SetToBlendOf(syncTrack0, syncTrack1, blendWeight);
                blendedSyncTrack = ownedSyncTrack;
                Duration = SyncTrack.CalculateDurationSynchronized(blendSource0.Duration, blendSource1.Duration, syncTrack0.NumEvents, syncTrack1.NumEvents, blendedSyncTrack.NumEvents, blendWeight);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (!IsValid)
            {
                return base.Update(ctx);
            }

            EvaluateBlendSpace(ctx);

            // Calculate the actual update range: an externally supplied one, or this frame's time
            // step converted to a range on the blended sync track.
            SyncTrackTimeRange range;
            if (updateRange != null)
            {
                range = updateRange.Value;
            }
            else
            {
                var deltaPercentage = Duration > 0f ? ctx.DeltaTime / Duration : 0f;
                var fromTime = CurrentTime;
                var toTime = AllowLooping ? CurrentTime + deltaPercentage : MathUtils.Saturate(CurrentTime + deltaPercentage);
                range = new SyncTrackTimeRange(blendedSyncTrack!.GetTime(fromTime), blendedSyncTrack.GetTime(toTime));
            }

            // Single source
            if (blendSource1 == null)
            {
                if (blendSource0 is not { IsValid: true })
                {
                    return base.Update(ctx);
                }

                var single = blendSource0.Update(ctx, range);
                PreviousTime = SyncTrack.GetPercentageThrough(range.StartTime);
                CurrentTime = SyncTrack.GetPercentageThrough(range.EndTime);
                return single;
            }

            // 2-way blend into our own buffer: both sources advance through the same sync range
            var result = base.Update(ctx);

            var result0 = blendSource0!.Update(ctx, range);
            var result1 = blendSource1.Update(ctx, range);

            Blender.Blend(result0.Pose, result1.Pose, blendWeight, result.Pose);
            result.RootMotionDelta = Blender.BlendRootMotion(result0.RootMotionDelta, result1.RootMotionDelta, blendWeight, RootMotionBlendMode.Blend);
            result.SampledEventRange = ctx.SampledEvents.BlendEventRanges(result0.SampledEventRange, result1.SampledEventRange, blendWeight);

            PreviousTime = SyncTrack.GetPercentageThrough(range.StartTime);
            CurrentTime = SyncTrack.GetPercentageThrough(range.EndTime);

            return result;
        }
    }

    partial class Blend1DNode
    {
        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);

            // Blend space is evaluated lazily on the first Update — the input parameter node may not be
            // initialized yet at this point (nodes initialize in array order).
            ActiveParameterization = Parameterization;
        }
    }

    partial class VelocityBlendNode
    {
        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);

            // TODO: parameterization should be built from each source clip's average linear velocity, which
            // comes from decoded root motion (not yet available). Fall back to even index spacing so the
            // node still blends across its sources.
            ctx.LogWarning(NodeIdx, "VelocityBlend parameterization falling back to index spacing (clip average velocity not yet available).");

            var values = new float[SourceNodes.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = i;
            }

            ActiveParameterization = ParameterizedBlendNode__Parameterization.CreateParameterization(values);
        }
    }

    // Blends source pose nodes across a 2D parameter space using a precomputed triangulation
    // (barycentric blend inside a triangle, projection onto the nearest hull edge outside).
    partial class Blend2DNode
    {
        struct BlendSpaceResult
        {
            public int Src0;
            public int Src1;
            public int Src2;
            public float Weight01;
            public float Weight12;

            public void Reset()
            {
                Src0 = Src1 = Src2 = -1;
                Weight01 = Weight12 = 0f;
            }
        }

        public PoseNode[] SourceNodes;

        public override void Restart(GraphContext ctx)
        {
            base.Restart(ctx);

            if (SourceNodes == null)
            {
                return;
            }

            foreach (var sourceNode in SourceNodes)
            {
                sourceNode.Restart(ctx);
            }
        }
        public FloatValueNode InputParameterNode0;
        public FloatValueNode InputParameterNode1;

        BlendSpaceResult bsr;
        uint blendSpaceUpdateID = uint.MaxValue;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(SourceNodeIndices, ref SourceNodes);
            ctx.SetNodeFromIndex(InputParameterNodeIdx0, ref InputParameterNode0);
            ctx.SetNodeFromIndex(InputParameterNodeIdx1, ref InputParameterNode1);

            // Blend space is evaluated lazily on the first Update (input parameter nodes may not be
            // initialized yet at this point).
        }

        // Esoterica Blend2D requires every source to be valid (unlike Blend1D)
        public override bool IsValid
        {
            get
            {
                if (SourceNodes is not { Length: > 1 } || InputParameterNode0 == null || InputParameterNode1 == null)
                {
                    return false;
                }

                foreach (var source in SourceNodes)
                {
                    if (!source.IsValid)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        SyncTrack? blendedSyncTrack;
        readonly SyncTrack ownedSyncTrack2Way = SyncTrack.CreateBlendScratch();
        readonly SyncTrack ownedSyncTrack3Way = SyncTrack.CreateBlendScratch();

        public override SyncTrack SyncTrack => blendedSyncTrack ?? SyncTrack.Default;

        void EvaluateBlendSpace(GraphContext ctx)
        {
            // Only evaluate the blend space once per graph update.
            if (blendSpaceUpdateID == ctx.UpdateID)
            {
                return;
            }

            blendSpaceUpdateID = ctx.UpdateID;

            var point = new Vector2(InputParameterNode0.GetValue(ctx), InputParameterNode1.GetValue(ctx));
            CalculateBlendSpaceWeights(Values, Indices, HullIndices, point, ref bsr);

            // Calculate the blended sync track and synchronized duration across the up-to-3 sources
            if (bsr.Src1 == -1)
            {
                var source = SourceNodes[bsr.Src0];
                ownedSyncTrack2Way.SetToBlendOf(source.SyncTrack, source.SyncTrack, 0f);
                blendedSyncTrack = ownedSyncTrack2Way;
                Duration = source.Duration;
            }
            else
            {
                var source0 = SourceNodes[bsr.Src0];
                var source1 = SourceNodes[bsr.Src1];
                var syncTrack0 = source0.SyncTrack;
                var syncTrack1 = source1.SyncTrack;

                ownedSyncTrack2Way.SetToBlendOf(syncTrack0, syncTrack1, bsr.Weight01);
                blendedSyncTrack = ownedSyncTrack2Way;
                Duration = SyncTrack.CalculateDurationSynchronized(source0.Duration, source1.Duration, syncTrack0.NumEvents, syncTrack1.NumEvents, blendedSyncTrack.NumEvents, bsr.Weight01);

                if (bsr.Src2 != -1)
                {
                    var source2 = SourceNodes[bsr.Src2];
                    var syncTrack2 = source2.SyncTrack;
                    var durationOf2WayBlend = Duration;
                    var numEventsIn2WayBlendedSyncTrack = blendedSyncTrack.NumEvents;

                    ownedSyncTrack3Way.SetToBlendOf(ownedSyncTrack2Way, syncTrack2, bsr.Weight12);
                    blendedSyncTrack = ownedSyncTrack3Way;
                    Duration = SyncTrack.CalculateDurationSynchronized(durationOf2WayBlend, source2.Duration, numEventsIn2WayBlendedSyncTrack, syncTrack2.NumEvents, blendedSyncTrack.NumEvents, bsr.Weight12);
                }
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (!IsValid)
            {
                return base.Update(ctx);
            }

            EvaluateBlendSpace(ctx);

            // Calculate the actual update range: an externally supplied one, or this frame's time
            // step converted to a range on the blended sync track.
            SyncTrackTimeRange range;
            if (updateRange != null)
            {
                range = updateRange.Value;
            }
            else
            {
                var deltaPercentage = Duration > 0f ? ctx.DeltaTime / Duration : 0f;
                var fromTime = CurrentTime;
                var toTime = AllowLooping ? CurrentTime + deltaPercentage : MathUtils.Saturate(CurrentTime + deltaPercentage);
                range = new SyncTrackTimeRange(blendedSyncTrack!.GetTime(fromTime), blendedSyncTrack.GetTime(toTime));
            }

            // Single source
            if (bsr.Src1 == -1)
            {
                var source = SourceNodes[bsr.Src0];
                if (!source.IsValid)
                {
                    return base.Update(ctx);
                }

                var single = source.Update(ctx, range);
                PreviousTime = SyncTrack.GetPercentageThrough(range.StartTime);
                CurrentTime = SyncTrack.GetPercentageThrough(range.EndTime);
                return single;
            }

            // 2- or 3-way blend into our own buffer: all sources advance through the same sync range
            var result = base.Update(ctx);

            var s0 = SourceNodes[bsr.Src0];
            var s1 = SourceNodes[bsr.Src1];
            var r0 = s0.Update(ctx, range);
            var r1 = s1.Update(ctx, range);

            Blender.Blend(r0.Pose, r1.Pose, bsr.Weight01, result.Pose);
            result.RootMotionDelta = Blender.BlendRootMotion(r0.RootMotionDelta, r1.RootMotionDelta, bsr.Weight01, RootMotionBlendMode.Blend);
            result.SampledEventRange = ctx.SampledEvents.BlendEventRanges(r0.SampledEventRange, r1.SampledEventRange, bsr.Weight01);

            if (bsr.Src2 != -1)
            {
                var s2 = SourceNodes[bsr.Src2];
                var r2 = s2.Update(ctx, range);

                Blender.Blend(result.Pose, r2.Pose, bsr.Weight12, result.Pose);
                result.RootMotionDelta = Blender.BlendRootMotion(result.RootMotionDelta, r2.RootMotionDelta, bsr.Weight12, RootMotionBlendMode.Blend);
                result.SampledEventRange = ctx.SampledEvents.BlendEventRanges(result.SampledEventRange, r2.SampledEventRange, bsr.Weight12);
            }

            PreviousTime = SyncTrack.GetPercentageThrough(range.StartTime);
            CurrentTime = SyncTrack.GetPercentageThrough(range.EndTime);

            return result;
        }

        static void CalculateBlendSpaceWeights(Vector2[] points, uint[] indices, uint[] hullIndices, Vector2 point, ref BlendSpaceResult result)
        {
            result.Reset();

            var enclosingTriangleFound = false;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = (int)indices[i];
                int i1 = (int)indices[i + 1];
                int i2 = (int)indices[i + 2];

                if (CalculateBarycentricCoordinates(point, points[i0], points[i1], points[i2], out var bcc))
                {
                    // Sort the three contributions ascending by weight
                    var iw = new (int Idx, float Weight)[]
                    {
                        (i0, bcc.X),
                        (i1, bcc.Y),
                        (i2, bcc.Z),
                    };
                    Array.Sort(iw, (a, b) => a.Weight.CompareTo(b.Weight));

                    if (IsNearEqual(iw[2].Weight, 1f, 1e-4f))
                    {
                        result.Src0 = iw[2].Idx;
                        result.Src1 = result.Src2 = -1;
                        result.Weight01 = result.Weight12 = 0f;
                    }
                    else
                    {
                        result.Src0 = iw[0].Idx; // lowest
                        result.Src1 = iw[1].Idx;
                        result.Src2 = iw[2].Idx; // highest
                        result.Weight01 = iw[1].Weight / (iw[0].Weight + iw[1].Weight);
                        result.Weight12 = iw[2].Weight;
                    }

                    enclosingTriangleFound = true;
                    break;
                }
            }

            if (!enclosingTriangleFound)
            {
                // Find the nearest hull edge and project onto it (hull has its first index duplicated at the end)
                var closestDistance = float.MaxValue;
                var closestStartHullIdx = -1;
                var closestT = 0f;

                for (var i = 1; i < hullIndices.Length; i++)
                {
                    var p0 = points[hullIndices[i - 1]];
                    var p1 = points[hullIndices[i]];
                    var cp = ClosestPointOnSegment(p0, p1, point, out var t);
                    var distance = Vector2.DistanceSquared(cp, point);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestStartHullIdx = i - 1;
                        closestT = t;
                    }
                }

                Debug.Assert(closestStartHullIdx >= 0);
                result.Src0 = (int)hullIndices[closestStartHullIdx];
                result.Src1 = (int)hullIndices[closestStartHullIdx + 1];
                result.Src2 = -1;
                result.Weight01 = closestT;
                result.Weight12 = 0f;
            }

            Debug.Assert(result.Src0 != -1);

            // Simplify away redundant blends
            if (IsNearEqual(result.Weight01, 1f, 1e-4f))
            {
                result.Src0 = result.Src1;
                result.Src1 = result.Src2;
                result.Src2 = -1;
                result.Weight01 = result.Weight12;
                result.Weight12 = 0f;
            }
            else if (result.Weight01 < 1e-4f)
            {
                if (result.Src2 == -1)
                {
                    result.Src1 = -1;
                }
                else
                {
                    result.Src0 = result.Src1;
                    result.Src1 = result.Src2;
                    result.Src2 = -1;
                    result.Weight01 = result.Weight12;
                    result.Weight12 = 0f;
                }
            }
        }

        static bool IsNearEqual(float a, float b, float epsilon = 1e-5f) => MathF.Abs(a - b) <= epsilon;

        static bool CalculateBarycentricCoordinates(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 bary)
        {
            var v0 = b - a;
            var v1 = c - a;
            var v2 = p - a;

            var d00 = Vector2.Dot(v0, v0);
            var d01 = Vector2.Dot(v0, v1);
            var d11 = Vector2.Dot(v1, v1);
            var d20 = Vector2.Dot(v2, v0);
            var d21 = Vector2.Dot(v2, v1);

            var denom = d00 * d11 - d01 * d01;
            if (MathF.Abs(denom) < 1e-10f)
            {
                bary = default;
                return false;
            }

            var v = (d11 * d20 - d01 * d21) / denom;
            var w = (d00 * d21 - d01 * d20) / denom;
            var u = 1f - v - w;

            bary = new Vector3(u, v, w);

            const float edgeEpsilon = -1e-4f;
            return u >= edgeEpsilon && v >= edgeEpsilon && w >= edgeEpsilon;
        }

        static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p, out float t)
        {
            var ab = b - a;
            var lengthSq = Vector2.Dot(ab, ab);
            t = lengthSq <= 1e-10f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0f, 1f);
            return a + ab * t;
        }
    }

    // Layers additional pose sources over a base pose. Port of Esoterica's LayerBlendNode: each
    // layer runs in a fresh layer context; state machine layers always update and contribute their
    // state's authored weight and bone mask through the context.
    partial class LayerBlendNode
    {
        PoseNode BaseNode;
        PoseNode?[] LayerInputs = [];
        FloatValueNode?[] LayerWeights = [];
        BoneMaskValueNode?[] LayerMasks = [];

        // Scratch buffers for model-space layer blending
        Quaternion[] baseGlobalRotations = [];
        Quaternion[] layerGlobalRotations = [];
        Quaternion[] resultGlobalRotations = [];
        FrameBone[] maskedLayerResult = [];
        float[] layerMaskWeights = [];

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodeFromIndex(BaseNodeIdx, ref BaseNode);

            LayerInputs = new PoseNode?[LayerDefinition.Length];
            LayerWeights = new FloatValueNode?[LayerDefinition.Length];
            LayerMasks = new BoneMaskValueNode?[LayerDefinition.Length];

            for (var i = 0; i < LayerDefinition.Length; i++)
            {
                ctx.SetOptionalNodeFromIndex(LayerDefinition[i].InputNodeIdx, ref LayerInputs[i]);
                ctx.SetOptionalNodeFromIndex(LayerDefinition[i].WeightValueNodeIdx, ref LayerWeights[i]);
                ctx.SetOptionalNodeFromIndex(LayerDefinition[i].BoneMaskValueNodeIdx, ref LayerMasks[i]);
            }

            baseGlobalRotations = new Quaternion[PoseTransforms.Length];
            layerGlobalRotations = new Quaternion[PoseTransforms.Length];
            resultGlobalRotations = new Quaternion[PoseTransforms.Length];
            maskedLayerResult = new FrameBone[PoseTransforms.Length];
            layerMaskWeights = new float[PoseTransforms.Length];
        }

        public override bool IsValid => BaseNode?.IsValid ?? false;

        public override SyncTrack SyncTrack => BaseNode?.SyncTrack ?? SyncTrack.Default;

        public override void Restart(GraphContext ctx)
        {
            base.Restart(ctx);
            BaseNode?.Restart(ctx);

            foreach (var layerInput in LayerInputs)
            {
                layerInput?.Restart(ctx);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var eventRangeStart = ctx.SampledEvents.Count;
            var result = base.Update(ctx);

            var baseResult = BaseNode.Update(ctx, updateRange);
            Duration = BaseNode.Duration;
            PreviousTime = BaseNode.PreviousTime;
            CurrentTime = BaseNode.CurrentTime;

            // Synchronized layers advance through the same sync range the base covered this update
            var baseSyncTrack = BaseNode.SyncTrack;
            var layerUpdateRange = new SyncTrackTimeRange(baseSyncTrack.GetTime(BaseNode.PreviousTime), baseSyncTrack.GetTime(BaseNode.CurrentTime));

            // Blend layers into our own buffer so the base node's pose is left untouched
            baseResult.Pose.AsSpan(0, Math.Min(baseResult.Pose.Length, PoseTransforms.Length)).CopyTo(PoseTransforms);
            result.RootMotionDelta = baseResult.RootMotionDelta;

            // Cache any outer layer context so it can be restored after our layers are done
            var wasInLayer = ctx.IsInLayer;
            var outerLayerContext = ctx.LayerContext;
            var outerWeight = outerLayerContext.Weight;
            var outerRootMotionWeight = outerLayerContext.RootMotionWeight;
            var outerMask = outerLayerContext.MaskTaskList;

            for (var i = 0; i < LayerDefinition.Length; i++)
            {
                var layerInput = LayerInputs[i];
                if (layerInput == null)
                {
                    continue;
                }

                var definition = LayerDefinition[i];

                // Start a new layer
                ctx.IsInLayer = true;
                ctx.LayerContext.Reset();

                // If we're not a state machine, set up the layer context here
                if (!definition.IsStateMachineLayer)
                {
                    if (LayerWeights[i] != null)
                    {
                        ctx.LayerContext.Weight = MathUtils.Saturate(LayerWeights[i]!.GetValue(ctx));
                    }

                    if (LayerMasks[i] != null)
                    {
                        ctx.LayerContext.MaskTaskList.CopyFrom(LayerMasks[i]!.GetValue(ctx));
                    }
                }

                // Always update state machine layers as the transitions need to be evaluated;
                // they calculate the final layer weight and store it in the layer context.
                var updated = false;
                GraphPoseNodeResult layerResult = default;
                if (definition.IsStateMachineLayer || ctx.LayerContext.Weight > 0f)
                {
                    layerResult = layerInput.Update(ctx, definition.IsSynchronized ? layerUpdateRange : null);
                    updated = true;
                }

                var weight = MathUtils.Saturate(ctx.LayerContext.Weight);
                var mask = ctx.LayerContext.MaskTaskList;

                if (!updated || weight <= 1e-4f)
                {
                    continue;
                }

                var blendMode = definition.BlendMode;

                // We cannot perform a global blend without a bone mask
                if (blendMode == PoseBlendMode.ModelSpace && !mask.HasTasks)
                {
                    ctx.LogWarning(NodeIdx, "Attempting to perform a global blend without a bone mask! This is not supported so falling back to a local blend!");
                    blendMode = PoseBlendMode.Overlay;
                }

                // Evaluate the layer's bone mask task list into per-bone weights
                var hasMask = mask.HasTasks;
                if (hasMask)
                {
                    mask.GenerateBoneMask(ctx.Skeleton, ctx.BoneMaskPool, layerMaskWeights);
                }

                // Weight or discard the layer's sampled events (Esoterica UpdateLayers)
                if (definition.IgnoreEvents)
                {
                    ctx.SampledEvents.MarkEventsAsIgnored(layerResult.SampledEventRange);
                }
                else
                {
                    ctx.SampledEvents.UpdateWeights(layerResult.SampledEventRange, weight);
                }

                // Blend layer root motion unless only the base's is sampled
                if (!OnlySampleBaseRootMotion)
                {
                    var rootMotionWeight = weight * ctx.LayerContext.RootMotionWeight;
                    var rootMotionMode = definition.BlendMode == PoseBlendMode.Additive ? RootMotionBlendMode.Additive : RootMotionBlendMode.Blend;
                    result.RootMotionDelta = Blender.BlendRootMotion(result.RootMotionDelta, layerResult.RootMotionDelta, rootMotionWeight, rootMotionMode);
                }

                var boneCount = Math.Min(PoseTransforms.Length, layerResult.Pose.Length);

                switch (blendMode)
                {
                    case PoseBlendMode.Overlay:
                    case PoseBlendMode.Additive:
                        for (var b = 0; b < boneCount; b++)
                        {
                            var boneWeight = weight * (hasMask ? layerMaskWeights[b] : 1f);
                            if (boneWeight <= 0f)
                            {
                                continue;
                            }

                            PoseTransforms[b] = blendMode == PoseBlendMode.Additive
                                ? PoseTransforms[b].BlendAdd(layerResult.Pose[b], boneWeight)
                                : PoseTransforms[b].Blend(layerResult.Pose[b], boneWeight);
                        }

                        break;

                    case PoseBlendMode.ModelSpace:
                        BlendLayerModelSpace(ctx, layerResult.Pose, weight, layerMaskWeights, boneCount);
                        break;
                }
            }

            ctx.IsInLayer = wasInLayer;
            ctx.LayerContext = outerLayerContext;
            ctx.LayerContext.Weight = outerWeight;
            ctx.LayerContext.RootMotionWeight = outerRootMotionWeight;
            ctx.LayerContext.MaskTaskList = outerMask;

            result.Pose = PoseTransforms;
            result.SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);
            return result;
        }

        /// <summary>
        /// Port of Esoterica's Blender::ModelSpaceBlend: masked bones blend their rotations in model
        /// space (translation and scale blend in parent space), and the mask-weighted result is then
        /// blended over the base pose in parent space by the layer weight.
        /// </summary>
        private void BlendLayerModelSpace(GraphContext ctx, FrameBone[] layerPose, float layerWeight, float[] maskWeights, int boneCount)
        {
            var parentIndices = ctx.Skeleton?.ParentIndices;
            if (parentIndices == null || boneCount == 0)
            {
                return;
            }

            // Calculate global rotations for base and layer poses.
            // Bones are ordered parent-before-child; global = parentGlobal * local (System.Numerics order).
            baseGlobalRotations[0] = PoseTransforms[0].Angle;
            layerGlobalRotations[0] = layerPose[0].Angle;

            for (var b = 1; b < boneCount; b++)
            {
                var parentIdx = parentIndices[b];
                baseGlobalRotations[b] = baseGlobalRotations[parentIdx] * PoseTransforms[b].Angle;
                layerGlobalRotations[b] = layerGlobalRotations[parentIdx] * layerPose[b].Angle;
            }

            // Blend the root separately - parent space blend
            var rootWeight = maskWeights[0];
            if (rootWeight > 0f)
            {
                maskedLayerResult[0] = PoseTransforms[0].Blend(layerPose[0], rootWeight);
                resultGlobalRotations[0] = maskedLayerResult[0].Angle;
            }
            else
            {
                maskedLayerResult[0] = PoseTransforms[0];
                resultGlobalRotations[0] = PoseTransforms[0].Angle;
            }

            // Blend model-space rotations together and convert back to parent space
            for (var b = 1; b < boneCount; b++)
            {
                var boneWeight = maskWeights[b];
                if (boneWeight <= 0f)
                {
                    // Use the base local pose for masked out bones
                    resultGlobalRotations[b] = baseGlobalRotations[b];
                    maskedLayerResult[b] = PoseTransforms[b];
                }
                else
                {
                    // Translation and scale blend in parent space
                    var positionScale = Vector4.Lerp(PoseTransforms[b].PositionScale, layerPose[b].PositionScale, boneWeight);

                    // Rotation blends in model space
                    resultGlobalRotations[b] = Quaternion.Slerp(baseGlobalRotations[b], layerGlobalRotations[b], boneWeight);

                    var parentIdx = parentIndices[b];
                    var localRotation = Quaternion.Normalize(Quaternion.Inverse(resultGlobalRotations[parentIdx]) * resultGlobalRotations[b]);
                    maskedLayerResult[b] = new FrameBone(positionScale, localRotation);
                }
            }

            // Blend the masked result onto the base pose by the layer weight, in parent space
            for (var b = 0; b < boneCount; b++)
            {
                PoseTransforms[b] = PoseTransforms[b].Blend(maskedLayerResult[b], layerWeight);
            }
        }
    }
}
