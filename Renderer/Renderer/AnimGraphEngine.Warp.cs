using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Rotates a clip's root motion across its orientation warp event so the character ends up
    // facing a target direction.
    partial class OrientationWarpNode
    {
        ClipReferenceNode ClipReferenceNode;
        ValueNode TargetValueNode;

        RootMotionData warpedRootMotion = RootMotionData.Empty;
        bool shouldUpdateWarp;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodeFromIndex(ClipReferenceNodeIdx, ref ClipReferenceNode);
            ctx.SetNodeFromIndex(TargetValueNodeIdx, ref TargetValueNode);
        }

        public override bool IsValid => ClipReferenceNode.IsValid;

        public override SyncTrack SyncTrack => ClipReferenceNode.IsValid ? ClipReferenceNode.SyncTrack : SyncTrack.Default;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            ClipReferenceNode.Initialize(ctx, initialTime);
            TargetValueNode.Initialize(ctx);

            if (ClipReferenceNode.IsValid)
            {
                Duration = ClipReferenceNode.Duration;
                PreviousTime = ClipReferenceNode.PreviousTime;
                CurrentTime = ClipReferenceNode.CurrentTime;
                shouldUpdateWarp = true;
            }
            else
            {
                PreviousTime = CurrentTime = 0f;
                Duration = 0f;
                shouldUpdateWarp = false;
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            TargetValueNode.Shutdown(ctx);
            ClipReferenceNode.Shutdown(ctx);
            warpedRootMotion = RootMotionData.Empty;
            shouldUpdateWarp = false;

            base.ShutdownInternal(ctx);
        }

        void PerformWarp(GraphContext ctx)
        {
            warpedRootMotion = RootMotionData.Empty;

            var clip = ClipReferenceNode.GetClip(ctx);
            Debug.Assert(clip != null);
            if (clip == null)
            {
                return;
            }

            // Find the warp event
            var warpEvent = Array.Find(clip.Animation.Events, e => e.ClassName == "CNmOrientationWarpEvent");
            if (warpEvent == null)
            {
                ctx.LogWarning(NodeIdx, "No Orientation Warp Event Found!");
                return;
            }

            // Create the warp range and validate it given the current start time
            var clipDuration = clip.Duration;
            var warpEventEndTime = warpEvent.StartCycle + (clipDuration > 0f ? warpEvent.Duration / clipDuration : 0f);

            var currentTime = ClipReferenceNode.CurrentTime * clipDuration;
            var warpRangeStartTime = MathF.Max(warpEvent.StartCycle * clipDuration, currentTime);
            var warpRangeLength = (warpEventEndTime * clipDuration) - warpRangeStartTime;

            const float MinimumWarpPeriodAllowed = 1 / 30.0f;
            if (warpRangeLength < MinimumWarpPeriodAllowed)
            {
                ctx.LogWarning(NodeIdx, "Orientation Warp failed since there is not enough time left to warp or we're past the warp event!");
                return;
            }

            var warpStartTime = clip.GetFrameTime(clip.FrameCount == 1 ? 0f : warpRangeStartTime / clipDuration);
            var warpStartFrame = warpStartTime.NearestFrameIndex;

            var warpEndTime = clip.GetFrameTime(warpEventEndTime);
            var warpEndFrame = warpEndTime.UpperBoundFrameIndex;
            var numWarpFrames = warpEndFrame - warpStartFrame;

            Debug.Assert(numWarpFrames > 0);

            // Calculate targets
            var originalRootMotion = clip.RootMotion;
            Debug.Assert(originalRootMotion.IsValid);

            Vector3 postWarpOriginalDirCS;
            var endRotation = originalRootMotion.Transforms[^1].Angle;

            if (AlignmentMode == OrientationWarpNode__AlignmentMode.AnimationEndFacing)
            {
                postWarpOriginalDirCS = TransformMath.RotateVector(endRotation, TransformMath.WorldForward);
            }
            else
            {
                postWarpOriginalDirCS = originalRootMotion.Transforms[^1].Position - originalRootMotion.GetTransform(new FrameTime(warpEndFrame)).Position;

                // Without a valid direction use the end orientation
                postWarpOriginalDirCS = TransformMath.IsNearZero(postWarpOriginalDirCS)
                    ? TransformMath.RotateVector(endRotation, TransformMath.WorldForward)
                    : Normalize2(postWarpOriginalDirCS);
            }

            // The target direction we need to align to
            Vector3 targetDirCS;

            if (IsOffsetNode)
            {
                var offset = float.DegreesToRadians(((FloatValueNode)TargetValueNode).GetValue(ctx));
                var offsetRotation = Quaternion.CreateFromAxisAngle(TransformMath.WorldUp, offset);

                targetDirCS = IsOffsetRelativeToCharacter
                    ? TransformMath.RotateVector(offsetRotation, TransformMath.WorldForward)
                    : TransformMath.RotateVector(offsetRotation, postWarpOriginalDirCS);
            }
            else
            {
                targetDirCS = ((VectorValueNode)TargetValueNode).GetValue(ctx);

                if (TransformMath.IsNearZero(targetDirCS))
                {
                    ctx.LogWarning(NodeIdx, "Orientation warp failed since there wasnt a valid target direction set!");
                    return;
                }

                targetDirCS = Normalize2(targetDirCS);
            }

            // The desired modification we need to make
            var desiredOrientationDelta = TransformMath.FromRotationBetweenUnitVectors(postWarpOriginalDirCS, targetDirCS);

            Transform[] transforms;
            int numFrames;

            if (originalRootMotion.IsStationary)
            {
                numFrames = clip.FrameCount;
                transforms = new Transform[numFrames];

                // Set initial world space positions up to the end of the rotation warp event
                for (var i = 0; i <= warpEndFrame; i++)
                {
                    transforms[i] = ctx.WorldTransform;
                }

                // Distribute the desired orientation delta among the warped frames
                for (var i = warpStartFrame + 1; i <= warpEndFrame; i++)
                {
                    var percentage = (float)(i - warpStartFrame) / numWarpFrames;
                    var frameDelta = Quaternion.Slerp(Quaternion.Identity, desiredOrientationDelta, percentage);
                    transforms[i].Angle = frameDelta * transforms[i].Angle;
                }

                // Set the remaining root motion to the result of the last frame
                for (var i = warpEndFrame + 1; i < numFrames; i++)
                {
                    transforms[i] = transforms[warpEndFrame];
                }
            }
            else
            {
                numFrames = originalRootMotion.NumFrames;
                transforms = (Transform[])originalRootMotion.Transforms.Clone();

                // Set start transform
                transforms[0] = ctx.WorldTransform;

                // Set initial world space positions up to the end of the rotation warp event
                for (var i = 1; i <= warpEndFrame; i++)
                {
                    var originalDelta = TransformMath.Delta(originalRootMotion.Transforms[i - 1], originalRootMotion.Transforms[i]);
                    transforms[i] = originalDelta * transforms[i - 1];
                }

                // Spread out the delta rotation across the entire warp section
                for (var i = warpStartFrame + 1; i <= warpEndFrame; i++)
                {
                    var percentage = (float)(i - warpStartFrame) / numWarpFrames;
                    var frameDelta = Quaternion.Slerp(Quaternion.Identity, desiredOrientationDelta, percentage);
                    transforms[i].Angle = transforms[i].Angle * frameDelta;
                }

                // Adjust the remaining root motion relative to the warped frames
                for (var i = warpEndFrame + 1; i < numFrames; i++)
                {
                    var originalDelta = TransformMath.Delta(originalRootMotion.Transforms[i - 1], originalRootMotion.Transforms[i]);
                    transforms[i] = originalDelta * transforms[i - 1];
                }
            }

            warpedRootMotion = new RootMotionData(transforms, numFrames, originalRootMotion);
        }

        // Normalizes by the length on the ground plane
        static Vector3 Normalize2(Vector3 v)
        {
            var length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));
            return length > 0f ? v / length : v;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (!IsValid)
            {
                return base.Update(ctx);
            }

            if (shouldUpdateWarp)
            {
                PerformWarp(ctx);
                shouldUpdateWarp = false;
            }

            var result = ClipReferenceNode.Update(ctx, updateRange);
            Duration = ClipReferenceNode.Duration;
            PreviousTime = ClipReferenceNode.PreviousTime;
            CurrentTime = ClipReferenceNode.CurrentTime;

            if (warpedRootMotion.IsValid)
            {
                result.RootMotionDelta = warpedRootMotion.SampleRootMotion(SamplingMode, ctx.WorldTransform, PreviousTime, CurrentTime);
            }

            return result;
        }
    }
}
