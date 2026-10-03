using System.Diagnostics;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Warps a clip's root motion across its target warp events so the character arrives at a target.
    partial class TargetWarpNode
    {
        const float CurveBlowoutDetectionThreshold = 2.5f;
        const float MinAllowedRootMotionScale = 0.05f;

        enum InternalState
        {
            RequiresInitialUpdate,
            AllowUpdates,
            Completed,
            Failed,
        }

        sealed class WarpSection
        {
            public int StartFrame;
            public int EndFrame;
            public Transform DeltaTransform = Transform.Identity;

            // The total progress along the section for each frame
            public float[] TotalProgress = [];
            public float DistanceCovered;
            public TargetWarpRule WarpRule;
            public TargetWarpAlgorithm TranslationAlgorithm;
            public bool HasTranslation;

            public bool IsFixedSection => WarpRule == TargetWarpRule.FixedSection;

            // At least 3 frames in length, i.e. a single warp frame
            public bool HasValidFrameRange => EndFrame > StartFrame + 1;

            // Including the last frame
            public int NumWarpableFrames => EndFrame - StartFrame;
        }

        ClipReferenceNode ClipReferenceNode;
        TargetValueNode TargetValueNode;

        RootMotionData__SamplingMode samplingMode = RootMotionData__SamplingMode.WorldSpace;
        InternalState internalState = InternalState.RequiresInitialUpdate;

        int alignmentBoneIdx = -1;
        int translationXYSectionIdx = -1;
        int rotationSectionIdx = -1;
        bool isTranslationAllowedZ;
        int numSectionZ;
        int totalNumWarpableZFrames;

        FrameTime warpStartTime;
        Transform warpStartWorldTransform = Transform.Identity;

        // The warp target that was requested, and the one we can achieve based on the events
        Transform requestedWarpTarget = Transform.Identity;
        Transform warpTarget = Transform.Identity;

        Transform[] warpedTransforms = [];
        RootMotionData warpedRootMotion = RootMotionData.Empty;

        // Scratch data for the calculations
        readonly List<Transform> deltaTransforms = [];
        readonly List<Transform> inverseDeltaTransforms = [];
        readonly List<WarpSection> warpSections = [];
        Pose? alignmentPose;
        Transform[] alignmentPoseBuffer = [];

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodeFromIndex(ClipReferenceNodeIdx, ref ClipReferenceNode);
            ctx.SetNodeFromIndex(TargetValueNodeIdx, ref TargetValueNode);

            if (AlignmentBoneID.IsValid)
            {
                alignmentBoneIdx = ctx.Skeleton.GetBoneIndex(AlignmentBoneID);

                if (alignmentBoneIdx == -1)
                {
                    ctx.LogWarning(NodeIdx, "Cant find specified alignment bone ID for target warp node", AlignmentBoneID);
                }
                else
                {
                    alignmentPose = new Pose(ctx.Skeleton);
                    alignmentPoseBuffer = new Transform[PoseTransforms.Length];
                }
            }
        }

        public override bool IsValid => ClipReferenceNode.IsValid && Duration > 0f;

        public override SyncTrack SyncTrack => ClipReferenceNode.SyncTrack;

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

                if (Duration == 0f)
                {
                    ctx.LogWarning(NodeIdx, "Zero frame animations are not supported for warping!");
                }
            }
            else
            {
                PreviousTime = CurrentTime = 0f;
                Duration = 0f;
            }

            ClearWarpInfo();
            internalState = InternalState.RequiresInitialUpdate;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ClearWarpInfo();
            TargetValueNode.Shutdown(ctx);
            ClipReferenceNode.Shutdown(ctx);

            base.ShutdownInternal(ctx);
        }

        bool TryReadTarget(GraphContext ctx)
        {
            var target = TargetValueNode.GetValue(ctx);
            if (!target.IsSet || target.IsBoneTarget)
            {
                ctx.LogWarning(NodeIdx, "Invalid target detected for warp node!");
                return false;
            }

            requestedWarpTarget = target.Transform;
            return true;
        }

        static Quaternion CalculateWarpedOrientationReferenceQuat(Transform[] transforms, int frameIdx)
        {
            Debug.Assert(frameIdx > 0 && frameIdx < transforms.Length);

            var referenceDirection = TransformMath.Normalize2(transforms[frameIdx].Position - transforms[frameIdx - 1].Position);
            if (referenceDirection.X == 0f && referenceDirection.Y == 0f)
            {
                // Use the previous frame's orientation as a fallback
                return transforms[frameIdx - 1].Angle;
            }

            if (MathF.Abs(Vector3.Dot(referenceDirection, TransformMath.WorldForward) - (-1f)) <= 1e-3f)
            {
                return TransformMath.LookAt(referenceDirection, TransformMath.RotateVector(transforms[frameIdx].Angle, TransformMath.WorldUp));
            }

            return TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, referenceDirection);
        }

        static Quaternion CalculateWarpedOrientationForFrame(Transform[] originalTransforms, Transform[] warpedTransforms, int frameIdx)
        {
            // The relative orientation in the original root motion
            var originalReferenceQuat = CalculateWarpedOrientationReferenceQuat(originalTransforms, frameIdx);
            var originalRotationOffset = Quaternion.Inverse(originalReferenceQuat) * originalTransforms[frameIdx].Angle;

            var warpedReferenceQuat = CalculateWarpedOrientationReferenceQuat(warpedTransforms, frameIdx);
            return warpedReferenceQuat * originalRotationOffset;
        }

        void SolveFixedSection(WarpSection section, Transform startTransform)
        {
            Debug.Assert(section.IsFixedSection);

            warpedTransforms[section.StartFrame] = startTransform;

            for (var f = section.StartFrame + 1; f <= section.EndFrame; f++)
            {
                warpedTransforms[f] = deltaTransforms[f] * warpedTransforms[f - 1];
            }
        }

        void SolveTranslationZSection(WarpSection section, Transform startTransform, float correctionZ)
        {
            Debug.Assert(section.WarpRule == TargetWarpRule.WarpZ);

            warpedTransforms[section.StartFrame] = startTransform;

            for (var f = section.StartFrame + 1; f <= section.EndFrame; f++)
            {
                warpedTransforms[f] = deltaTransforms[f] * warpedTransforms[f - 1];
                var stepIdx = f - section.StartFrame;
                var progressThisFrame = section.TotalProgress[stepIdx] - section.TotalProgress[stepIdx - 1];
                warpedTransforms[f].Position += new Vector3(0f, 0f, correctionZ * progressThisFrame);
            }
        }

        void SolveRotationSection(WarpSection section, Transform startTransform, Quaternion correction)
        {
            Debug.Assert(section.WarpRule == TargetWarpRule.RotationOnly);

            warpedTransforms[section.StartFrame] = startTransform;

            // Generate base root motion
            for (var f = section.StartFrame + 1; f <= section.EndFrame; f++)
            {
                warpedTransforms[f] = deltaTransforms[f] * warpedTransforms[f - 1];
            }

            // Modify facing
            for (var f = section.StartFrame + 1; f <= section.EndFrame; f++)
            {
                var frameDelta = Quaternion.Slerp(Quaternion.Identity, correction, section.TotalProgress[f - section.StartFrame]);
                warpedTransforms[f].Angle = warpedTransforms[f].Angle * frameDelta;
            }
        }

        void SolveTranslationSection(GraphContext ctx, GraphClip clip, WarpSection section, Transform startTransform, Transform endTransform)
        {
            Debug.Assert(section.WarpRule is TargetWarpRule.WarpXY or TargetWarpRule.WarpXYZ);

            var originalTransforms = clip.RootMotion.Transforms;

            var startPoint = startTransform.Position;
            var endPoint = endTransform.Position;
            var desiredDistanceToCover = Vector3.Distance(startPoint, endPoint);

            warpedTransforms[section.StartFrame] = startTransform;
            warpedTransforms[section.EndFrame] = endTransform;

            // Fallback checks
            var triggerFallback = false;

            // With no displacement, anything we do will look weird, so just lerp
            if (MathF.Abs(section.DistanceCovered) <= TransformMath.Epsilon)
            {
                triggerFallback = true;
                ctx.LogWarning(NodeIdx, "Target Warp: trying to XY warp a section with no displacement, falling back to LERP!");
            }

            // Under the distance threshold fitting a curve looks ugly, so just lerp
            if (!triggerFallback && desiredDistanceToCover <= LerpFallbackDistanceThreshold)
            {
                triggerFallback = true;
                ctx.LogWarning(NodeIdx, "Target Warp: trying to XY warp to under the distance threshold, falling back to LERP!");
            }

            bool EnsureValidCurve(float curveLength)
            {
                // A curve much longer than the straight distance likely has loops or blowouts
                var guideCurveRatio = curveLength / desiredDistanceToCover;
                if (guideCurveRatio >= CurveBlowoutDetectionThreshold)
                {
                    ctx.LogWarning(NodeIdx, "Target Warp: XY warp curve blowout detected! Falling back to LERP!");
                    triggerFallback = true;
                }

                return !triggerFallback;
            }

            var numWarpFrames = section.EndFrame - section.StartFrame;

            // Basic curve generation
            if (!triggerFallback && section.TranslationAlgorithm is TargetWarpAlgorithm.Hermite or TargetWarpAlgorithm.Bezier)
            {
                var startTangent = TransformMath.NormalizeOrZero((deltaTransforms[section.StartFrame + 1] * startTransform).Position - startTransform.Position);
                var endTangent = TransformMath.NormalizeOrZero(endTransform.Position - (inverseDeltaTransforms[section.EndFrame] * endTransform).Position);

                // With tangents in the same direction but moving against the start tangent, just lerp
                if (TransformMath.IsVectorInTheSameHemisphere2D(startTangent, endTangent)
                    && Vector3.Dot(endPoint - startPoint, startTangent) <= 0f)
                {
                    triggerFallback = true;
                    ctx.LogWarning(NodeIdx, "Target Warp: Trying to XY warp backwards, falling back to LERP!");
                }

                if (!triggerFallback)
                {
                    // Scale tangents to half the distance, clamped to the max length
                    var scalingFactor = MathF.Min(MaxTangentLength, Vector3.Distance(endPoint, startPoint) / 2);
                    startTangent *= scalingFactor;
                    endTangent *= scalingFactor;

                    if (section.TranslationAlgorithm == TargetWarpAlgorithm.Hermite)
                    {
                        var curveLength = CubicHermite.GetSplineLength(startPoint, startTangent, endPoint, endTangent);
                        if (EnsureValidCurve(curveLength))
                        {
                            for (var i = 1; i < numWarpFrames; i++)
                            {
                                var frameIdx = section.StartFrame + i;
                                warpedTransforms[frameIdx].Position = CubicHermite.GetPoint(startPoint, startTangent, endPoint, endTangent, section.TotalProgress[i]);
                                warpedTransforms[frameIdx].Angle = CalculateWarpedOrientationForFrame(originalTransforms, warpedTransforms, frameIdx);
                            }
                        }
                    }
                    else
                    {
                        var cp0 = startPoint + startTangent;
                        var cp1 = endPoint - endTangent;

                        var curveLength = CubicBezier.GetEstimatedLength(startPoint, cp0, cp1, endPoint);
                        if (EnsureValidCurve(curveLength))
                        {
                            for (var i = 1; i < numWarpFrames; i++)
                            {
                                var frameIdx = section.StartFrame + i;
                                warpedTransforms[frameIdx].Position = CubicBezier.GetPoint(startPoint, cp0, cp1, endPoint, section.TotalProgress[i]);
                                warpedTransforms[frameIdx].Angle = CalculateWarpedOrientationForFrame(originalTransforms, warpedTransforms, frameIdx);
                            }
                        }
                    }
                }
            }
            // Feature preserving curve generation
            else if (section.TranslationAlgorithm == TargetWarpAlgorithm.HermiteFeaturePreserving)
            {
                // Guide curve start tangent
                var unwarpedEndPoint = (section.DeltaTransform * startTransform).Position;
                var startToUnwarpedEnd = unwarpedEndPoint - startTransform.Position;
                var sectionDeltaDistance = startToUnwarpedEnd.Length();
                var hermiteStartTangent = TransformMath.NormalizeOrZero(startToUnwarpedEnd);

                // Guide curve end tangent, ideally the direction of the next segment of the path to smooth out the motion
                Vector3 hermiteEndTangent;
                if (section.EndFrame + 1 < deltaTransforms.Count)
                {
                    var endPlusOne = deltaTransforms[section.EndFrame + 1] * endTransform;
                    hermiteEndTangent = TransformMath.NormalizeOrZero(endPlusOne.Position - endPoint);
                }
                else // The last segment uses the entry direction
                {
                    var endMinusOne = inverseDeltaTransforms[section.EndFrame - 1] * endTransform;
                    hermiteEndTangent = TransformMath.NormalizeOrZero(endPoint - endMinusOne.Position);
                }

                if (TransformMath.IsNearZero(hermiteEndTangent))
                {
                    hermiteEndTangent = TransformMath.RotateVector(endTransform.Angle, TransformMath.WorldForward);
                }

                var scalingFactor = MathF.Min(MaxTangentLength, Vector3.Distance(endPoint, startPoint) / 2);
                hermiteStartTangent *= scalingFactor;
                hermiteEndTangent *= scalingFactor;

                // Original root motion scale factor
                var curveLength = CubicHermite.GetSplineLength(startPoint, hermiteStartTangent, endPoint, hermiteEndTangent);
                var rootMotionScaleFactor = curveLength / sectionDeltaDistance;
                if (rootMotionScaleFactor < MinAllowedRootMotionScale)
                {
                    ctx.LogWarning(NodeIdx, "Target Warp: Feature preserving XY warp required scaling is lower than the minimum allowed threshold. Falling back to LERP!");
                    triggerFallback = true;
                }

                if (!triggerFallback && EnsureValidCurve(curveLength))
                {
                    var sectionDeltaOrientation = TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, TransformMath.NormalizeOrZero(hermiteStartTangent));
                    var scaledUnwarpedEndPoint = unwarpedEndPoint * rootMotionScaleFactor;

                    var currentTransform = startTransform;
                    for (var i = 1; i <= numWarpFrames; i++)
                    {
                        var frameIdx = section.StartFrame + i;
                        var percentageAlongCurve = section.TotalProgress[i];

                        // The original path delta from the displacement line
                        currentTransform = deltaTransforms[frameIdx] * currentTransform;
                        var scaledCurrentTransform = new Transform(currentTransform.Position * rootMotionScaleFactor, 1f, currentTransform.Angle);
                        var straightTransform = new Transform(Vector3.Lerp(startPoint, scaledUnwarpedEndPoint, percentageAlongCurve), 1f, sectionDeltaOrientation);
                        var curveDelta = TransformMath.Delta(straightTransform, scaledCurrentTransform);

                        // The new curved path
                        var (point, tangent) = CubicHermite.GetPointAndTangent(startPoint, hermiteStartTangent, endPoint, hermiteEndTangent, percentageAlongCurve);
                        var curveTangentOrientation = TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, TransformMath.Normalize2(tangent));
                        var curveTangentTransform = new Transform(point, 1f, curveTangentOrientation);

                        warpedTransforms[frameIdx] = curveDelta * curveTangentTransform;
                    }
                }
            }

            // Lerp or fallback
            if (triggerFallback || section.TranslationAlgorithm == TargetWarpAlgorithm.Lerp)
            {
                for (var i = 1; i < numWarpFrames; i++)
                {
                    var frameIdx = section.StartFrame + i;
                    warpedTransforms[frameIdx] = TransformMath.Lerp(startTransform, endTransform, section.TotalProgress[i]);
                }
            }
        }

        void ClearWarpInfo()
        {
            warpedTransforms = [];
            warpedRootMotion = RootMotionData.Empty;
            warpSections.Clear();
            deltaTransforms.Clear();
            inverseDeltaTransforms.Clear();
        }

        static bool IsBefore(FrameTime a, FrameTime b)
            => a.FrameIndex < b.FrameIndex || (a.FrameIndex == b.FrameIndex && a.PercentageThrough < b.PercentageThrough);

        static bool IsAtStart(FrameTime time) => time.FrameIndex == 0 && time.PercentageThrough == 0f;

        Transform GetClipModelSpaceTransform(GraphClip clip, int frameIndex, int boneIdx)
        {
            Debug.Assert(alignmentPose != null);
            clip.SamplePoseAtFrame(frameIndex, alignmentPoseBuffer);
            alignmentPose.SetParentSpaceTransforms(alignmentPoseBuffer);
            return alignmentPose.GetModelSpaceTransform(boneIdx);
        }

        bool GenerateWarpInfo(GraphContext ctx, GraphClip clip)
        {
            Debug.Assert(warpSections.Count == 0);

            var numFrames = clip.FrameCount;
            var originalRM = clip.RootMotion;
            Debug.Assert(originalRM.IsValid);

            // Read warp events
            warpStartTime = clip.GetFrameTime(ClipReferenceNode.CurrentTime);

            var clipStartFrame = warpStartTime.FrameIndex;
            var minimumStartFrameForFirstSection = clipStartFrame + 1;
            var clipDuration = clip.Duration;

            foreach (var clipEvent in clip.Animation.Events)
            {
                // Immediate events cannot be warp events
                if (clipEvent.Duration <= 0f)
                {
                    continue;
                }

                var eventEndPercentage = clipEvent.StartCycle + (clipDuration > 0f ? clipEvent.Duration / clipDuration : 0f);

                // Skip any events that are before our start time
                var eventEndTime = clip.GetFrameTime(eventEndPercentage);
                if (IsBefore(eventEndTime, warpStartTime))
                {
                    continue;
                }

                // A section per warp event
                if (clipEvent.ClassName != "CNmTargetWarpEvent")
                {
                    continue;
                }

                var section = new WarpSection
                {
                    StartFrame = clip.GetFrameTime(clipEvent.StartCycle).NearestFrameIndex,
                    EndFrame = Math.Min(numFrames, eventEndTime.NearestFrameIndex),
                    WarpRule = Enum.TryParse<TargetWarpRule>(clipEvent.Data.GetStringProperty("m_rule", string.Empty), out var rule) ? rule : TargetWarpRule.WarpXY,
                    TranslationAlgorithm = Enum.TryParse<TargetWarpAlgorithm>(clipEvent.Data.GetStringProperty("m_algorithm", string.Empty), out var algorithm) ? algorithm : TargetWarpAlgorithm.Lerp,
                };

                // Adjust start frame to animation start time
                if (minimumStartFrameForFirstSection >= section.StartFrame)
                {
                    section.StartFrame = minimumStartFrameForFirstSection;

                    // Skip 1 frame events
                    if (!section.HasValidFrameRange)
                    {
                        continue;
                    }
                }

                Debug.Assert(section.StartFrame < section.EndFrame);

                // Insert a fixed section between non adjacent warp events
                if (warpSections.Count > 0 && warpSections[^1].EndFrame != section.StartFrame)
                {
                    warpSections.Add(new WarpSection
                    {
                        StartFrame = warpSections[^1].EndFrame,
                        EndFrame = section.StartFrame,
                        WarpRule = TargetWarpRule.FixedSection,
                    });
                }

                warpSections.Add(section);

                // Track the options for this warp
                switch (section.WarpRule)
                {
                    case TargetWarpRule.WarpXY:
                        translationXYSectionIdx = warpSections.Count - 1;
                        break;

                    case TargetWarpRule.WarpZ:
                        isTranslationAllowedZ = true;
                        break;

                    case TargetWarpRule.WarpXYZ:
                        translationXYSectionIdx = warpSections.Count - 1;
                        isTranslationAllowedZ = true;
                        break;

                    case TargetWarpRule.RotationOnly:
                        rotationSectionIdx = warpSections.Count - 1;
                        break;
                }
            }

            if (warpSections.Count == 0)
            {
                ctx.LogWarning(NodeIdx, "No valid warp events detected! Either no events present or start time is after warp events.");
                return false;
            }

            if (translationXYSectionIdx == -1 && !isTranslationAllowedZ)
            {
                ctx.LogWarning(NodeIdx, "Warp attempted for animation with invalid warp events!");
                warpSections.Clear();
                return false;
            }

            // Per frame root motion deltas, frames before the first section are never used
            for (var i = 0; i < minimumStartFrameForFirstSection; i++)
            {
                deltaTransforms.Add(Transform.Identity);
                inverseDeltaTransforms.Add(Transform.Identity);
            }

            for (var i = minimumStartFrameForFirstSection; i < numFrames; i++)
            {
                var delta = TransformMath.DeltaNoScale(originalRM.Transforms[i - 1], originalRM.Transforms[i]);
                deltaTransforms.Add(delta);
                inverseDeltaTransforms.Add(delta.Inverse());
            }

            // Section info
            numSectionZ = 0;
            totalNumWarpableZFrames = 0;

            foreach (var section in warpSections)
            {
                section.DeltaTransform = TransformMath.Delta(originalRM.Transforms[section.StartFrame], originalRM.Transforms[section.EndFrame]);

                // Fixed sections have no length
                if (section.IsFixedSection)
                {
                    continue;
                }

                section.TotalProgress = new float[section.EndFrame - section.StartFrame + 1];
                section.DistanceCovered = 0f;

                if (section.WarpRule == TargetWarpRule.RotationOnly)
                {
                    for (var i = section.StartFrame + 1; i <= section.EndFrame; i++)
                    {
                        section.HasTranslation |= deltaTransforms[i].Position.LengthSquared() > 0f;
                        var angularDistance = MathF.Abs(TransformMath.GetAngle(deltaTransforms[i].Angle));
                        section.TotalProgress[i - section.StartFrame] = angularDistance;
                        section.DistanceCovered += angularDistance;
                    }
                }
                else // Translation
                {
                    for (var i = section.StartFrame + 1; i <= section.EndFrame; i++)
                    {
                        var distance = deltaTransforms[i].Position.Length();
                        section.TotalProgress[i - section.StartFrame] = distance;
                        section.DistanceCovered += distance;
                    }

                    if (section.WarpRule is TargetWarpRule.WarpZ or TargetWarpRule.WarpXYZ)
                    {
                        numSectionZ++;
                        totalNumWarpableZFrames += section.NumWarpableFrames;
                    }

                    section.HasTranslation = section.DistanceCovered > 0f;
                }

                // Convert progress from distance to cumulative percentage
                if (section.DistanceCovered > 0f)
                {
                    for (var i = 1; i < section.TotalProgress.Length; i++)
                    {
                        section.TotalProgress[i] /= section.DistanceCovered;
                        section.TotalProgress[i] += section.TotalProgress[i - 1];
                        section.TotalProgress[i] = MathF.Min(section.TotalProgress[i], 1f);
                    }
                }
                else // Each frame has the exact same contribution
                {
                    var numWarpFrames = section.TotalProgress.Length;
                    var contributionPerFrame = 1f / (numWarpFrames - 1);
                    for (var i = 1; i < numWarpFrames - 1; i++)
                    {
                        section.TotalProgress[i] = section.TotalProgress[i - 1] + contributionPerFrame;
                    }

                    section.TotalProgress[^1] = 1f;
                }
            }

            // The actual target
            warpTarget = requestedWarpTarget;

            if (alignmentBoneIdx != -1 || !isTranslationAllowedZ)
            {
                var startWorldTransform = ctx.WorldTransform;

                // Offset the warp start transform by the distance already covered due to a later start time
                if (!IsAtStart(warpStartTime))
                {
                    var rootTransformAtActualStartTime = originalRM.GetTransform(warpStartTime);
                    startWorldTransform = rootTransformAtActualStartTime.Inverse() * startWorldTransform;
                }

                var requestedWarpTargetCS = requestedWarpTarget * startWorldTransform.Inverse();
                var warpTargetCS = requestedWarpTargetCS;

                var lastWarpSection = warpSections[^1];

                var targetAlignmentFrame = AlignWithTargetAtLastWarpEvent ? lastWarpSection.EndFrame : numFrames - 1;
                var originalRootMotionFrame = originalRM.IsStationary ? 0 : targetAlignmentFrame;

                // To align the alignment bone with the target, the root needs to be offset by the bone at the alignment time
                var alignmentBoneOffset = alignmentBoneIdx != -1
                    ? GetClipModelSpaceTransform(clip, targetAlignmentFrame, alignmentBoneIdx)
                    : Transform.Identity;

                var unwarpedTargetCS = alignmentBoneOffset * originalRM.Transforms[originalRootMotionFrame];

                // Without horizontal translation, just keep the target's Z value
                if (translationXYSectionIdx == -1)
                {
                    var adjustedTranslation = unwarpedTargetCS.Position;
                    adjustedTranslation.Z = requestedWarpTargetCS.Position.Z;
                    warpTargetCS.Position = adjustedTranslation;
                }

                // Without vertical translation, just keep the target's XY value
                if (!isTranslationAllowedZ)
                {
                    var adjustedTranslation = requestedWarpTargetCS.Position;
                    adjustedTranslation.Z = unwarpedTargetCS.Position.Z;
                    warpTargetCS.Position = adjustedTranslation;
                }

                warpTarget = warpTargetCS * startWorldTransform;
            }

            return true;
        }

        void GenerateWarpedRootMotion(GraphContext ctx, GraphClip clip)
        {
            var numFrames = clip.FrameCount;
            var originalRM = clip.RootMotion;
            Debug.Assert(originalRM.IsValid);

            // Prepare for warping and handle start time
            samplingMode = SamplingMode;
            warpStartWorldTransform = ctx.WorldTransform;

            warpedTransforms = new Transform[originalRM.NumFrames];
            warpedRootMotion = new RootMotionData(warpedTransforms, originalRM.NumFrames);

            // Offset the warp start transform by the distance already covered due to a later start time
            if (!IsAtStart(warpStartTime))
            {
                var rootTransformAtActualStartTime = originalRM.GetTransform(warpStartTime);
                warpStartWorldTransform = rootTransformAtActualStartTime.Inverse() * warpStartWorldTransform;
            }

            // All unwarped start frames
            for (var i = 0; i <= warpSections[0].StartFrame; i++)
            {
                warpedTransforms[i] = originalRM.Transforms[i];
            }

            var lastWarpSection = warpSections[^1];
            var targetAlignmentFrameIdx = AlignWithTargetAtLastWarpEvent ? lastWarpSection.EndFrame : numFrames - 1;

            // The frame at which we want to be aligned with the target
            warpedTransforms[targetAlignmentFrameIdx] = warpStartWorldTransform.Inverse() * warpTarget;

            // Trailing unwarped frames
            for (var frameIdx = targetAlignmentFrameIdx + 1; frameIdx < numFrames; frameIdx++)
            {
                warpedTransforms[frameIdx] = warpedTransforms[frameIdx - 1] * deltaTransforms[frameIdx];
            }

            for (var frameIdx = targetAlignmentFrameIdx - 1; frameIdx >= lastWarpSection.EndFrame; frameIdx--)
            {
                warpedTransforms[frameIdx] = warpedTransforms[frameIdx + 1] * inverseDeltaTransforms[frameIdx + 1];
            }

            var numSections = warpSections.Count;

            // The delta between where we end up and where we want to be
            var warpDelta = TransformMath.Delta(originalRM.Transforms[lastWarpSection.EndFrame], warpedTransforms[lastWarpSection.EndFrame]);

            float GetCorrectionZ(WarpSection warpSection)
            {
                var correctionZ = warpDelta.Position.Z;
                if (numSectionZ > 1)
                {
                    correctionZ *= (float)warpSection.NumWarpableFrames / totalNumWarpableZFrames;
                }

                return correctionZ;
            }

            // Forward solve, stopping on rotation or XY sections
            var rIdx = rotationSectionIdx == -1 ? numSections : rotationSectionIdx;
            var tIdx = translationXYSectionIdx == -1 ? numSections : translationXYSectionIdx;
            var forwardEndIdx = Math.Min(rIdx, tIdx);
            for (var i = 0; i < forwardEndIdx; i++)
            {
                var warpSection = warpSections[i];
                if (warpSection.WarpRule == TargetWarpRule.WarpZ)
                {
                    SolveTranslationZSection(warpSection, warpedTransforms[warpSection.StartFrame], GetCorrectionZ(warpSection));
                }
                else if (warpSection.IsFixedSection)
                {
                    SolveFixedSection(warpSection, warpedTransforms[warpSection.StartFrame]);
                }
            }

            // Backward solve, stopping on the XY section
            var backwardsEndIdx = Math.Min(numSections - 1, tIdx);
            for (var i = numSections - 1; i > backwardsEndIdx; i--)
            {
                var warpSection = warpSections[i];
                if (warpSection.WarpRule == TargetWarpRule.WarpZ)
                {
                    var correctionZ = GetCorrectionZ(warpSection);
                    var sectionStartTransform = warpSection.DeltaTransform.Inverse() * warpedTransforms[warpSection.EndFrame];
                    sectionStartTransform.Position += new Vector3(0f, 0f, -correctionZ);
                    SolveTranslationZSection(warpSection, sectionStartTransform, correctionZ);
                }
                else if (warpSection.WarpRule == TargetWarpRule.RotationOnly)
                {
                    // The start transform for this section, which determines the target of the XY (T) section
                    var targetTransform = warpedTransforms[warpSection.EndFrame];
                    var startTransformT = warpedTransforms[warpSections[tIdx].StartFrame];

                    // The delta between the XY section and the rotation (including the rotation section)
                    var unwarpedDeltaPostT = warpSections[tIdx + 1].DeltaTransform;
                    for (var j = tIdx + 2; j <= i; j++)
                    {
                        unwarpedDeltaPostT = warpSections[j].DeltaTransform * unwarpedDeltaPostT;
                    }

                    Vector3 alignmentDir;

                    // Align all the sections post T to the shortest path between target and start of T,
                    // or to the direction to the end point
                    var startToTarget = targetTransform.Position - startTransformT.Position;
                    if (TransformMath.IsNearZero2(startToTarget))
                    {
                        alignmentDir = warpedTransforms[^1].Position - targetTransform.Position;

                        alignmentDir = TransformMath.IsNearZero2(alignmentDir)
                            ? TransformMath.RotateVector(warpedTransforms[^1].Angle, TransformMath.WorldForward)
                            : TransformMath.Normalize2(alignmentDir);
                    }
                    else
                    {
                        alignmentDir = TransformMath.Normalize2(startToTarget);
                    }

                    // The new start point for all the sections post T
                    var sectionStartTransform = Transform.Identity;
                    var deltaDistance = TransformMath.Length2(unwarpedDeltaPostT.Position);
                    sectionStartTransform.Position = targetTransform.Position - (alignmentDir * deltaDistance);

                    // Put the section start at the correct height so it doesn't get doubled
                    sectionStartTransform.Position += -TransformMath.WorldUp * unwarpedDeltaPostT.Position.Z;

                    // The incoming movement orientation allows placing the translation section on the last frame of translation
                    var unwarpedMovementOrientation = originalRM.GetIncomingMovementOrientation2DAtFrame(warpSections[tIdx].EndFrame);
                    var originalFacingOrientation = originalRM.Transforms[warpSections[tIdx].EndFrame].Angle;
                    var offset = Quaternion.Inverse(unwarpedMovementOrientation) * originalFacingOrientation;
                    sectionStartTransform.Angle = TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, alignmentDir) * offset;

                    // The start transform for this section, applying the delta of each section
                    for (var j = tIdx + 1; j < i; j++)
                    {
                        sectionStartTransform = warpSections[j].DeltaTransform * sectionStartTransform;
                    }

                    // The required delta for the section
                    var estimatedSectionEndTransform = warpSection.DeltaTransform * sectionStartTransform;
                    var requiredCorrection = Quaternion.Inverse(estimatedSectionEndTransform.Angle) * targetTransform.Angle;

                    SolveRotationSection(warpSection, sectionStartTransform, requiredCorrection);
                }
                else if (warpSection.IsFixedSection)
                {
                    var sectionStartTransform = warpSection.DeltaTransform.Inverse() * warpedTransforms[warpSection.EndFrame];
                    SolveFixedSection(warpSection, sectionStartTransform);
                }
            }

            // Final forward solve of the remaining (XY and rotation) sections
            for (var i = forwardEndIdx; i <= backwardsEndIdx; i++)
            {
                var warpSection = warpSections[i];
                if (warpSection.WarpRule == TargetWarpRule.RotationOnly)
                {
                    Quaternion requiredCorrection;

                    // Align to the XY section's end transform, which the backward pass set
                    var sectionStartTransform = warpedTransforms[warpSection.StartFrame];
                    var sectionEndTransform = warpSection.DeltaTransform * sectionStartTransform;
                    var targetTransform = warpedTransforms[warpSections[backwardsEndIdx].EndFrame];

                    // If we end up at the target at the end of the segment, align to end orientation
                    var toTarget = targetTransform.Position - sectionEndTransform.Position;
                    if (TransformMath.IsNearZero2(toTarget))
                    {
                        requiredCorrection = Quaternion.Inverse(sectionEndTransform.Angle) * targetTransform.Angle;
                    }
                    else
                    {
                        // The estimated unwarped movement
                        var endFramePlusOne = deltaTransforms[warpSection.EndFrame + 1] * sectionEndTransform;
                        var outgoingMovement = endFramePlusOne.Position - sectionEndTransform.Position;
                        if (TransformMath.IsNearZero2(outgoingMovement))
                        {
                            outgoingMovement = TransformMath.RotateVector(sectionEndTransform.Angle, TransformMath.WorldForward);
                        }

                        requiredCorrection = TransformMath.FromRotationBetweenUnitVectors(TransformMath.Normalize2(outgoingMovement), TransformMath.Normalize2(toTarget));
                    }

                    SolveRotationSection(warpSection, sectionStartTransform, requiredCorrection);
                }
                else if (warpSection.WarpRule is TargetWarpRule.WarpXY or TargetWarpRule.WarpXYZ)
                {
                    SolveTranslationSection(ctx, clip, warpSection, warpedTransforms[warpSection.StartFrame], warpedTransforms[warpSection.EndFrame]);
                }
                else if (warpSection.IsFixedSection)
                {
                    SolveFixedSection(warpSection, warpedTransforms[warpSection.StartFrame]);
                }
                else if (warpSection.WarpRule == TargetWarpRule.WarpZ)
                {
                    SolveTranslationZSection(warpSection, warpedTransforms[warpSection.StartFrame], GetCorrectionZ(warpSection));
                }
            }

            // Put the warped root motion in world space for the world space sampling mode
            for (var i = 0; i < warpedTransforms.Length; i++)
            {
                warpedTransforms[i] *= warpStartWorldTransform;
            }
        }

        bool UpdateWarp(GraphContext ctx, GraphClip clip)
        {
            var isRecalculationAllowed = TargetUpdateRule is TargetWarpNode__TargetUpdateRule.Recalculate or TargetWarpNode__TargetUpdateRule.RecalculateOrOffset;

            if (internalState is InternalState.Completed or InternalState.Failed)
            {
                if (TargetUpdateRule == TargetWarpNode__TargetUpdateRule.Offset && warpedRootMotion.IsValid)
                {
                    OffsetWarpedRootMotion(ctx);
                }

                return false;
            }

            if (internalState == InternalState.RequiresInitialUpdate)
            {
                if (!TryReadTarget(ctx))
                {
                    internalState = isRecalculationAllowed ? InternalState.AllowUpdates : InternalState.Failed;
                    return false;
                }
            }
            else // Update check
            {
                Debug.Assert(internalState == InternalState.AllowUpdates);

                // Cannot update an invalid warp
                if (!warpedRootMotion.IsValid)
                {
                    return false;
                }

                // Cannot update a warp that is past all the warp events
                var currentFrameIdx = clip.GetFrameTime(CurrentTime).UpperBoundFrameIndex;
                if (!CanRecalculateWarpedRootMotion(currentFrameIdx))
                {
                    if (TargetUpdateRule == TargetWarpNode__TargetUpdateRule.RecalculateOrOffset)
                    {
                        OffsetWarpedRootMotion(ctx);
                    }

                    return false;
                }

                // Has the target changed?
                var previousRequestedTarget = requestedWarpTarget;
                if (!TryReadTarget(ctx))
                {
                    return false;
                }

                var deltaAngle = TransformMath.Distance(requestedWarpTarget.Angle, previousRequestedTarget.Angle);
                var shouldUpdate = deltaAngle > TargetUpdateAngleThresholdRadians;

                if (!shouldUpdate && translationXYSectionIdx != -1)
                {
                    var deltaDistanceXY = TransformMath.Length2(requestedWarpTarget.Position - previousRequestedTarget.Position);
                    shouldUpdate = deltaDistanceXY > TargetUpdateDistanceThreshold;
                }

                if (!shouldUpdate && isTranslationAllowedZ)
                {
                    var deltaDistanceZ = requestedWarpTarget.Position.Z - previousRequestedTarget.Position.Z;
                    shouldUpdate = deltaDistanceZ > TargetUpdateDistanceThreshold;
                }

                // Without a change, keep the original requested target
                if (!shouldUpdate)
                {
                    requestedWarpTarget = previousRequestedTarget;
                    return false;
                }
            }

            // Calculate the warp
            ClearWarpInfo();

            if (GenerateWarpInfo(ctx, clip))
            {
                GenerateWarpedRootMotion(ctx, clip);
                internalState = isRecalculationAllowed ? InternalState.AllowUpdates : InternalState.Completed;
                return true;
            }

            internalState = isRecalculationAllowed ? InternalState.AllowUpdates : InternalState.Failed;
            return false;
        }

        bool OffsetWarpedRootMotion(GraphContext ctx)
        {
            var previousRequestedTarget = requestedWarpTarget;
            if (!TryReadTarget(ctx))
            {
                return false;
            }

            if (TransformMath.IsNearEqual(requestedWarpTarget, previousRequestedTarget))
            {
                requestedWarpTarget = previousRequestedTarget;
                return false;
            }

            var delta = previousRequestedTarget.Inverse() * requestedWarpTarget;
            for (var i = 0; i < warpedTransforms.Length; i++)
            {
                warpedTransforms[i] *= delta;
            }

            warpStartWorldTransform *= delta;
            warpTarget *= delta;

            return true;
        }

        bool CanRecalculateWarpedRootMotion(int startFrameIdx)
        {
            var hasXYSection = false;
            var hasZSection = false;

            foreach (var warpSection in warpSections)
            {
                if (warpSection.EndFrame <= startFrameIdx)
                {
                    continue;
                }

                switch (warpSection.WarpRule)
                {
                    case TargetWarpRule.WarpXY:
                        hasXYSection = true;
                        break;

                    case TargetWarpRule.WarpZ:
                        hasZSection = true;
                        break;

                    case TargetWarpRule.WarpXYZ:
                        hasZSection = true;
                        hasXYSection = true;
                        break;
                }
            }

            return hasXYSection && hasZSection;
        }

        void SampleWarpedRootMotion(GraphContext ctx, ref GraphPoseNodeResult result)
        {
            // If we failed to warp, keep the original root motion delta
            if (!warpedRootMotion.IsValid)
            {
                return;
            }

            // Accurate sampling matches the exact world space position each update
            if (samplingMode == RootMotionData__SamplingMode.WorldSpace)
            {
                var expectedTransform = warpedRootMotion.GetTransform(PreviousTime);
                var positionErrorSq = Vector3.DistanceSquared(expectedTransform.Position, ctx.WorldTransform.Position);
                if (positionErrorSq <= SamplingPositionErrorThresholdSq)
                {
                    result.RootMotionDelta = warpedRootMotion.SampleRootMotion(RootMotionData__SamplingMode.WorldSpace, ctx.WorldTransform, PreviousTime, CurrentTime);
                }
                else // Exceeded the error threshold, so fall back to inaccurate sampling
                {
                    samplingMode = RootMotionData__SamplingMode.Delta;
                    ctx.LogWarning(NodeIdx, "Target warp exceed accurate sampling error threshold! Switching to inaccurate sampling!");
                }
            }

            if (samplingMode == RootMotionData__SamplingMode.Delta)
            {
                result.RootMotionDelta = warpedRootMotion.SampleRootMotion(RootMotionData__SamplingMode.Delta, ctx.WorldTransform, PreviousTime, CurrentTime);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var clip = ClipReferenceNode.GetClip(ctx);
            if (!IsValid || clip == null)
            {
                return base.Update(ctx);
            }

            // Generate the warped root motion if needed
            var wasWarpUpdated = UpdateWarp(ctx, clip);

            // Step time on the target node
            var result = ClipReferenceNode.Update(ctx, updateRange);
            Duration = ClipReferenceNode.Duration;
            PreviousTime = ClipReferenceNode.PreviousTime;
            CurrentTime = ClipReferenceNode.CurrentTime;

            if (wasWarpUpdated)
            {
                // Always sample in world space when the warp updates, which corrects any sampling error
                // due to the mid-frame delta calculation
                var savedSamplingMode = samplingMode;
                samplingMode = RootMotionData__SamplingMode.WorldSpace;
                SampleWarpedRootMotion(ctx, ref result);
                samplingMode = savedSamplingMode;
            }
            else
            {
                SampleWarpedRootMotion(ctx, ref result);
            }

            return result;
        }
    }
}
