using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Blends 2..N source pose nodes along a 1D parameter using a parameterization (set of blend ranges).
    // Unsynchronized: each source advances on its own time; sync-track alignment is a later refine pass.
    partial class ParameterizedBlendNode
    {
        public PoseNode[] SourceNodes;
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

        public override bool IsValid
        {
            get
            {
                if (SourceNodes == null || SourceNodes.Length <= 1 || InputParameterValueNode == null)
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
                    if (weight <= 0f)
                    {
                        blendSource0 = SourceNodes[range.InputIdx0];
                    }
                    else if (weight >= 1f)
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

            if (blendSource0 == null)
            {
                Duration = 0f;
            }
            else if (blendSource1 == null)
            {
                Duration = blendSource0.Duration;
            }
            else
            {
                Duration = MathUtils.Lerp(blendSource0.Duration, blendSource1.Duration, blendWeight);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            if (!IsValid)
            {
                return base.Update(ctx);
            }

            EvaluateBlendSpace(ctx);

            // Single source
            if (blendSource1 == null)
            {
                var single = blendSource0!.Update(ctx);
                Duration = blendSource0.Duration;
                PreviousTime = blendSource0.PreviousTime;
                CurrentTime = blendSource0.CurrentTime;
                return single;
            }

            // 2-way blend into our own buffer
            var result = base.Update(ctx);

            var result0 = blendSource0!.Update(ctx);
            var result1 = blendSource1.Update(ctx);

            Blender.Blend(result0.Pose, result1.Pose, blendWeight, result.Pose);
            result.RootMotionDelta = Blender.BlendRootMotion(result0.RootMotionDelta, result1.RootMotionDelta, blendWeight, RootMotionBlendMode.Blend);

            Duration = MathUtils.Lerp(blendSource0.Duration, blendSource1.Duration, blendWeight);
            PreviousTime = MathUtils.Lerp(blendSource0.PreviousTime, blendSource1.PreviousTime, blendWeight);
            CurrentTime = MathUtils.Lerp(blendSource0.CurrentTime, blendSource1.CurrentTime, blendWeight);

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

        public override bool IsValid
        {
            get
            {
                if (SourceNodes == null || SourceNodes.Length <= 1 || InputParameterNode0 == null || InputParameterNode1 == null)
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

            if (bsr.Src1 == -1)
            {
                Duration = SourceNodes[bsr.Src0].Duration;
            }
            else
            {
                Duration = MathUtils.Lerp(SourceNodes[bsr.Src0].Duration, SourceNodes[bsr.Src1].Duration, bsr.Weight01);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            if (!IsValid)
            {
                return base.Update(ctx);
            }

            EvaluateBlendSpace(ctx);

            // Single source
            if (bsr.Src1 == -1)
            {
                var source = SourceNodes[bsr.Src0];
                var single = source.Update(ctx);
                Duration = source.Duration;
                PreviousTime = source.PreviousTime;
                CurrentTime = source.CurrentTime;
                return single;
            }

            // 2-way blend into our own buffer
            var result = base.Update(ctx);

            var s0 = SourceNodes[bsr.Src0];
            var s1 = SourceNodes[bsr.Src1];
            var r0 = s0.Update(ctx);
            var r1 = s1.Update(ctx);

            Blender.Blend(r0.Pose, r1.Pose, bsr.Weight01, result.Pose);
            result.RootMotionDelta = Blender.BlendRootMotion(r0.RootMotionDelta, r1.RootMotionDelta, bsr.Weight01, RootMotionBlendMode.Blend);

            Duration = MathUtils.Lerp(s0.Duration, s1.Duration, bsr.Weight01);
            PreviousTime = MathUtils.Lerp(s0.PreviousTime, s1.PreviousTime, bsr.Weight01);
            CurrentTime = MathUtils.Lerp(s0.CurrentTime, s1.CurrentTime, bsr.Weight01);

            // 3-way blend: blend the 2-way result with the third source, in place.
            if (bsr.Src2 != -1)
            {
                var r2 = SourceNodes[bsr.Src2].Update(ctx);
                Blender.Blend(result.Pose, r2.Pose, bsr.Weight12, result.Pose);
                result.RootMotionDelta = Blender.BlendRootMotion(result.RootMotionDelta, r2.RootMotionDelta, bsr.Weight12, RootMotionBlendMode.Blend);
            }

            return result;
        }

        // Ported from Esoterica's Blend2DNode::CalculateBlendSpaceWeights.
        static void CalculateBlendSpaceWeights(Vector2[] points, byte[] indices, byte[] hullIndices, Vector2 point, ref BlendSpaceResult result)
        {
            result.Reset();

            var enclosingTriangleFound = false;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

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
                result.Src0 = hullIndices[closestStartHullIdx];
                result.Src1 = hullIndices[closestStartHullIdx + 1];
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
}
