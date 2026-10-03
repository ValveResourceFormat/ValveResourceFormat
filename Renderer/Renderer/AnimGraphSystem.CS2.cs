using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Nodes that only exist in the Source 2 version of the animation graph. Valve composes transforms
    // as A o B = B expressed in A's space, which is B * A with Transform.

    /// <summary>Rotation and angle helpers for nodes specific to the Source 2 graph.</summary>
    public static class ValveMath
    {
        /// <summary>The model space path from the root to a bone, root first.</summary>
        public static int BuildRootToBoneChain(Skeleton skeleton, int boneIdx, Span<int> chain)
        {
            var count = 0;
            for (var b = boneIdx; b != -1; b = skeleton.GetParentBoneIndex(b))
            {
                count++;
            }

            var i = count;
            for (var b = boneIdx; b != -1; b = skeleton.GetParentBoneIndex(b))
            {
                chain[--i] = b;
            }

            return count;
        }

        /// <summary>The rotation from one vector onto another, through their half vector.</summary>
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            var half = (from + to) * 0.5f;

            // Opposite vectors rotate 180 degrees around any perpendicular axis
            if (half.LengthSquared() <= 1.19e-7f)
            {
                return TransformMath.FromRotationBetweenUnitVectors(TransformMath.NormalizeOrZero(from), TransformMath.NormalizeOrZero(to));
            }

            var cross = Vector3.Cross(from, half);
            return Quaternion.Normalize(new Quaternion(cross, Vector3.Dot(from, half)));
        }

        /// <summary>Pitch (positive down) and yaw in degrees, as Source decomposes a rotation.</summary>
        public static (float Pitch, float Yaw) ToPitchYaw(Quaternion q)
        {
            var m = Matrix4x4.CreateFromQuaternion(q);

            // Source matrices are column major, its m[i][0] is the forward axis
            var forwardX = m.M11;
            var forwardY = m.M12;
            var forwardZ = m.M13;
            var xy = MathF.Sqrt((forwardX * forwardX) + (forwardY * forwardY));

            var pitch = float.RadiansToDegrees(MathF.Atan2(-forwardZ, xy));
            var yaw = xy > 0.001f
                ? float.RadiansToDegrees(MathF.Atan2(forwardY, forwardX))
                : float.RadiansToDegrees(MathF.Atan2(-m.M21, m.M22));

            return (pitch, yaw);
        }

        /// <summary>Wraps an angle in degrees into [-180, 180].</summary>
        public static float AngleNormalize(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }

    // Moves a bone onto another bone's model space transform, or just its rotation or translation.
    partial class FollowBoneNode
    {
        BoolValueNode? EnabledNode;
        int boneIdx = -1;
        int targetIdx = -1;
        Pose solvePose;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);
            solvePose = new Pose(ctx.Skeleton);
        }

        public override bool IsValid => (ChildNode?.IsValid ?? false) && boneIdx != -1 && targetIdx != -1 && boneIdx != targetIdx;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            EnabledNode?.Initialize(ctx);

            boneIdx = ctx.Skeleton.GetBoneIndex(Bone);
            targetIdx = ctx.Skeleton.GetBoneIndex(FollowTargetBone);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            boneIdx = targetIdx = -1;
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            if (!IsValid || (EnabledNode != null && !EnabledNode.GetValue(ctx)))
            {
                return result;
            }

            solvePose.SetParentSpaceTransforms(result.Pose);

            var followed = solvePose.GetModelSpaceTransform(targetIdx);

            // The partial modes take the bone's local value as a model space one, which is only
            // right under an identity parent
            if (Mode == FollowBoneMode.RotationOnly)
            {
                followed.Position = solvePose.GetTransform(boneIdx).Position;
            }
            else if (Mode == FollowBoneMode.TranslationOnly)
            {
                followed.Angle = solvePose.GetTransform(boneIdx).Angle;
            }

            var parentIdx = ctx.Skeleton.GetParentBoneIndex(boneIdx);
            solvePose.SetTransform(boneIdx, parentIdx == -1 ? followed : followed * solvePose.GetModelSpaceTransform(parentIdx).Inverse());

            solvePose.CopyParentSpaceTransformsTo(PoseTransforms);
            result.Pose = PoseTransforms;
            return result;
        }
    }

    // Emits its body group event every update while enabled, for the game to apply.
    partial class BodyGroupNode
    {
        BoolValueNode? EnabledNode;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            EnabledNode?.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            if (EnabledNode != null && !EnabledNode.GetValue(ctx))
            {
                return result;
            }

            ctx.SampledEvents.EmplaceAnimationEvent(NodeIdx, ClipEvent, 0f, ctx.BranchState == BranchState.Active);
            result.SampledEventRange = new(result.SampledEventRange.StartIdx, ctx.SampledEvents.Count);
            return result;
        }
    }

    // Turns a chain of bones so the end effector looks at a target: yaw and pitch spread over the chain
    // by the chain weights, then the effector is aimed exactly.
    partial class ChainLookatNode
    {
        const int MaxChainLength = 8;

        VectorValueNode LookatTargetNode;
        BoolValueNode? EnabledNode;

        int effectorBoneIdx = -1;
        int numBonesInChain;
        bool isValidSetup;
        BlendWeight blendWeight = new();
        Vector3 cachedTarget;
        Pose solvePose;
        Transform[] chainModel = [];
        int[] chainBones = [];

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodeFromIndex(LookatTargetNodeIdx, ref LookatTargetNode);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);

            solvePose = new Pose(ctx.Skeleton);
            chainModel = new Transform[PoseTransforms.Length];
            chainBones = new int[PoseTransforms.Length];

            effectorBoneIdx = ctx.Skeleton.GetBoneIndex(EndEffectorBoneID);
            if (effectorBoneIdx == -1)
            {
                ctx.LogWarning(NodeIdx, $"Cant find specified effector bone ID ('{EndEffectorBoneID}')");
                return;
            }

            isValidSetup = true;

            var n = 0;
            var parentIdx = ctx.Skeleton.GetParentBoneIndex(effectorBoneIdx);
            while (parentIdx != -1 && n < ChainLength)
            {
                n++;
                parentIdx = ctx.Skeleton.GetParentBoneIndex(parentIdx);
            }

            if (n < 2)
            {
                ctx.LogWarning(NodeIdx, "Not enough bones in the chain to solve the look at request");
                effectorBoneIdx = -1;
                isValidSetup = false;
                return;
            }

            if (n < ChainLength)
            {
                ctx.LogWarning(NodeIdx, "Not enough bones in the chain for the requested chain length");
            }

            numBonesInChain = Math.Min(n, MaxChainLength - 1);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            LookatTargetNode.Initialize(ctx);

            var isOn = true;
            if (EnabledNode != null)
            {
                EnabledNode.Initialize(ctx);
                isOn = EnabledNode.GetValue(ctx);
            }

            blendWeight.Reset(BlendTimeSeconds, isOn);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            LookatTargetNode.Shutdown(ctx);
            cachedTarget = default;
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            if (ChildNode is not { IsValid: true } || !isValidSetup)
            {
                return result;
            }

            if (LookatTargetNode is ControlParameterVectorNode targetParameter)
            {
                // A point straight ahead of the effector leaves the chain as animated
                solvePose.SetParentSpaceTransforms(result.Pose);
                var effector = solvePose.GetModelSpaceTransform(effectorBoneIdx);
                var ahead = effector.TransformPoint(EndEffectorOffset) + (50f * TransformMath.RotateVector(effector.Angle, EndEffectorForwardAxis));
                targetParameter.ReportHint(IsTargetInWorldSpace ? ctx.WorldTransform.TransformPoint(ahead) : ahead, IsTargetInWorldSpace);
            }

            // The enable input is not read while fading out
            var isOn = blendWeight.State != BlendWeight.BlendWeightState.TurningOff && (EnabledNode?.GetValue(ctx) ?? true);
            var weight = blendWeight.Update(ctx.DeltaTime, isOn);

            if (weight <= 0f)
            {
                return result;
            }

            // The target is frozen while fading out
            if (blendWeight.State != BlendWeight.BlendWeightState.TurningOff)
            {
                cachedTarget = LookatTargetNode.GetValue(ctx);
            }

            solvePose.SetParentSpaceTransforms(result.Pose);
            Solve(ctx, weight, cachedTarget);
            solvePose.CopyParentSpaceTransformsTo(PoseTransforms);
            result.Pose = PoseTransforms;
            return result;
        }

        float GetChainWeight(int chainIndexFromTop)
        {
            // The first weight is the effector's, the last the top bone's
            var weightIdx = numBonesInChain - 1 - chainIndexFromTop;
            return weightIdx >= 0 && weightIdx < ChainWeights.Length ? ChainWeights[weightIdx] : 0f;
        }

        void Solve(GraphContext ctx, float weight, Vector3 target)
        {
            var count = ValveMath.BuildRootToBoneChain(ctx.Skeleton, effectorBoneIdx, chainBones);

            for (var k = 0; k < count; k++)
            {
                var local = solvePose.GetTransform(chainBones[k]);
                chainModel[k] = k == 0 ? local : local * chainModel[k - 1];
            }

            var effector = chainModel[count - 1];
            var offsetPos = effector.TransformPoint(EndEffectorOffset);
            var forwardMS = TransformMath.RotateVector(effector.Angle, EndEffectorForwardAxis);

            var resolvedTarget = IsTargetInWorldSpace ? ctx.WorldTransformInverse.TransformPoint(target) : target;

            // Keep the target between 10 and 1500 units away
            var toTarget = resolvedTarget - offsetPos;
            var lengthSquared = toTarget.LengthSquared();
            var length = MathF.Abs(lengthSquared) <= 0.001f ? 0f : MathF.Sqrt(lengthSquared);

            if (MathF.Abs(length) <= 0.001f)
            {
                // The forward axis is used unrotated here
                resolvedTarget = offsetPos + (10f * EndEffectorForwardAxis);
            }
            else if (length < 10f)
            {
                resolvedTarget = offsetPos + (10f * toTarget / length);
            }
            else if (length > 1500f)
            {
                resolvedTarget = offsetPos + (1500f * toTarget / length);
            }

            var direction = TransformMath.NormalizeOrZero(resolvedTarget - offsetPos);
            var (pitch, yaw) = ValveMath.ToPitchYaw(ValveMath.FromTo(forwardMS, direction));
            yaw = ValveMath.AngleNormalize(yaw) * weight;
            pitch = ValveMath.AngleNormalize(pitch) * weight;

            var start = count - numBonesInChain;

            // Yaw around the model up axis, applied to the model space rotations
            if (MathF.Abs(yaw) > 0.001f)
            {
                for (var i = start; i < count; i++)
                {
                    var angle = float.DegreesToRadians(GetChainWeight(i - start) * yaw);
                    var delta = angle == 0f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
                    chainModel[i].Angle = delta * chainModel[i].Angle;

                    for (var j = i + 1; j < count; j++)
                    {
                        chainModel[j] = solvePose.GetTransform(chainBones[j]) * chainModel[j - 1];
                    }
                }

                var parent = start == 0 ? Transform.Identity : chainModel[start - 1];
                for (var i = start; i < count; i++)
                {
                    solvePose.SetTransform(chainBones[i], chainModel[i] * parent.Inverse());
                    parent = chainModel[i];
                }
            }

            // Pitch around each bone's local Z axis, applied to the local rotations
            if (MathF.Abs(pitch) > 0.001f)
            {
                for (var i = start; i < count; i++)
                {
                    var bone = chainBones[i];
                    var delta = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, float.DegreesToRadians(GetChainWeight(i - start) * pitch));
                    var local = solvePose.GetTransform(bone);
                    local.Angle *= delta;
                    solvePose.SetTransform(bone, local);

                    if (bone != 0 && i > 0)
                    {
                        chainModel[i] = local * chainModel[i - 1];
                    }
                }
            }

            // Aim the effector exactly, from the bone origin rather than the offset
            var e = chainModel[count - 1];
            var forward = TransformMath.RotateVector(e.Angle, EndEffectorForwardAxis);
            var aimDirection = TransformMath.NormalizeOrZero(resolvedTarget - e.Position);
            var aimRotation = ValveMath.FromTo(forward, aimDirection) * e.Angle;
            var finalRotation = TransformMath.FastSLerp(e.Angle, aimRotation, weight);

            var finalModel = new Transform(e.Position, e.Scale, finalRotation);
            solvePose.SetTransform(effectorBoneIdx, finalModel * chainModel[count - 2].Inverse());
        }
    }
}
