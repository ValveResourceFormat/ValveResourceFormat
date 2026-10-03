namespace ValveResourceFormat.Renderer.AnimLib
{
    // Solves a two bone chain ending in the effector bone towards a target.
    partial class TwoBoneIKNode
    {
        TargetValueNode EffectorTargetNode;
        BoolValueNode? EnabledNode;

        int effectorBoneIdx = -1;
        bool isValidSetup;
        BlendWeight ikWeight = new();
        Pose solvePose;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodeFromIndex(EffectorTargetNodeIdx, ref EffectorTargetNode);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);

            solvePose = new Pose(ctx.Skeleton);
            effectorBoneIdx = ctx.Skeleton.GetBoneIndex(EffectorBoneID);

            // The effector needs two parents to form the chain
            isValidSetup = false;
            if (effectorBoneIdx != -1)
            {
                var parentCount = 0;
                var parentIdx = ctx.Skeleton.GetParentBoneIndex(effectorBoneIdx);
                while (parentIdx != -1 && parentCount < 2)
                {
                    parentCount++;
                    parentIdx = ctx.Skeleton.GetParentBoneIndex(parentIdx);
                }

                if (parentCount == 2)
                {
                    isValidSetup = true;
                }
                else
                {
                    ctx.LogWarning(NodeIdx, $"Invalid effector bone ID ('{EffectorBoneID}') specified, there are not enough bones in the chain to solve the IK request");
                    effectorBoneIdx = -1;
                }
            }
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            EffectorTargetNode.Initialize(ctx);

            if (EnabledNode != null)
            {
                EnabledNode.Initialize(ctx);
                ikWeight.Reset(BlendTimeSeconds, EnabledNode.GetValue(ctx));
            }
            else
            {
                ikWeight.Reset(0f, true);
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            EffectorTargetNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            if (ChildNode is not { IsValid: true } || !isValidSetup)
            {
                return result;
            }

            if (EffectorTargetNode is ControlParameterTargetNode targetParameter)
            {
                solvePose.SetParentSpaceTransforms(result.Pose);
                targetParameter.ReportHint(GetEffectorHint(ctx, effectorBoneIdx), IsTargetInWorldSpace);
            }

            var isIKEnabled = EnabledNode?.GetValue(ctx) ?? true;
            var weight = ikWeight.Update(ctx.DeltaTime, isIKEnabled);

            if (weight <= 0f)
            {
                return result;
            }

            var effectorTarget = EffectorTargetNode.GetValue(ctx);
            if (!effectorTarget.IsSet)
            {
                return result;
            }

            if (effectorTarget.IsBoneTarget)
            {
                if (!effectorTarget.BoneID.IsValid)
                {
                    ctx.LogWarning(NodeIdx, "No input target bone ID specified");
                    return result;
                }

                if (ctx.Skeleton.GetBoneIndex(effectorTarget.BoneID) == -1)
                {
                    ctx.LogWarning(NodeIdx, "Invalid input target bone ID specified", effectorTarget.BoneID);
                    return result;
                }
            }

            // Solve on a copy, the child's buffer is its own sampling output
            solvePose.SetParentSpaceTransforms(result.Pose);

            effectorTarget.TryGetTransform(solvePose, out var targetTransform);

            // Convert to character space
            if (!effectorTarget.IsBoneTarget && IsTargetInWorldSpace)
            {
                targetTransform *= ctx.WorldTransformInverse;
            }

            TwoBoneSolver.Solve(solvePose, effectorBoneIdx, targetTransform, ChainRotationWeight, BlendMode, Math.Clamp(weight, 0f, 1f));

            solvePose.CopyParentSpaceTransformsTo(PoseTransforms);
            result.Pose = PoseTransforms;
            return result;
        }

        // The target that leaves the effector where the animation put it
        Transform GetEffectorHint(GraphContext ctx, int boneIdx)
        {
            var effector = solvePose.GetModelSpaceTransform(boneIdx);
            return IsTargetInWorldSpace ? effector * ctx.WorldTransform : effector;
        }
    }

    // Two bone IK on both feet, or on whichever foot has a target set.
    partial class FootIKNode
    {
        TargetValueNode LeftTargetNode;
        TargetValueNode RightTargetNode;
        BoolValueNode? EnabledNode;

        int leftFootBoneIdx = -1;
        int rightFootBoneIdx = -1;
        bool isValidSetup;
        BlendWeight ikWeight = new();
        Pose solvePose;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodeFromIndex(LeftTargetNodeIdx, ref LeftTargetNode);
            ctx.SetNodeFromIndex(RightTargetNodeIdx, ref RightTargetNode);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);

            solvePose = new Pose(ctx.Skeleton);
            leftFootBoneIdx = ctx.Skeleton.GetBoneIndex(LeftEffectorBoneID);
            rightFootBoneIdx = ctx.Skeleton.GetBoneIndex(RightEffectorBoneID);

            isValidSetup = leftFootBoneIdx != -1 && rightFootBoneIdx != -1;
            if (!isValidSetup)
            {
                return;
            }

            if (!HasTwoParents(ctx.Skeleton, leftFootBoneIdx))
            {
                ctx.LogWarning(NodeIdx, $"Invalid left effector bone ID ('{LeftEffectorBoneID}') specified, there are not enough bones in the chain to solve the IK request");
                leftFootBoneIdx = -1;
                isValidSetup = false;
            }

            if (!HasTwoParents(ctx.Skeleton, rightFootBoneIdx))
            {
                ctx.LogWarning(NodeIdx, $"Invalid right effector bone ID ('{RightEffectorBoneID}') specified, there are not enough bones in the chain to solve the IK request");
                rightFootBoneIdx = -1;
                isValidSetup = false;
            }
        }

        static bool HasTwoParents(Skeleton skeleton, int boneIdx)
        {
            var parentCount = 0;
            var parentIdx = skeleton.GetParentBoneIndex(boneIdx);
            while (parentIdx != -1 && parentCount < 2)
            {
                parentCount++;
                parentIdx = skeleton.GetParentBoneIndex(parentIdx);
            }

            return parentCount == 2;
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            LeftTargetNode.Initialize(ctx);
            RightTargetNode.Initialize(ctx);

            if (EnabledNode != null)
            {
                EnabledNode.Initialize(ctx);
                ikWeight.Reset(BlendTimeSeconds, EnabledNode.GetValue(ctx));
            }
            else
            {
                ikWeight.Reset(0f, true);
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            LeftTargetNode.Shutdown(ctx);
            RightTargetNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        bool ValidateTarget(GraphContext ctx, Target target)
        {
            if (!target.IsBoneTarget)
            {
                return true;
            }

            if (!target.BoneID.IsValid)
            {
                ctx.LogWarning(NodeIdx, "No effector bone ID specified in input target");
                return false;
            }

            if (ctx.Skeleton.GetBoneIndex(target.BoneID) == -1)
            {
                ctx.LogWarning(NodeIdx, "Invalid effector bone ID specified in input target", target.BoneID);
                return false;
            }

            return true;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            if (ChildNode is not { IsValid: true } || !isValidSetup)
            {
                return result;
            }

            if (LeftTargetNode is ControlParameterTargetNode || RightTargetNode is ControlParameterTargetNode)
            {
                solvePose.SetParentSpaceTransforms(result.Pose);
                (LeftTargetNode as ControlParameterTargetNode)?.ReportHint(GetEffectorHint(ctx, leftFootBoneIdx), IsTargetInWorldSpace);
                (RightTargetNode as ControlParameterTargetNode)?.ReportHint(GetEffectorHint(ctx, rightFootBoneIdx), IsTargetInWorldSpace);
            }

            var isIKEnabled = EnabledNode?.GetValue(ctx) ?? true;
            var weight = ikWeight.Update(ctx.DeltaTime, isIKEnabled);

            if (weight <= 0f)
            {
                return result;
            }

            var leftTarget = LeftTargetNode.GetValue(ctx);
            var rightTarget = RightTargetNode.GetValue(ctx);
            var blendWeight = Math.Clamp(weight, 0f, 1f);

            // Solve on a copy, the child's buffer is its own sampling output
            solvePose.SetParentSpaceTransforms(result.Pose);

            if (leftTarget.IsSet && rightTarget.IsSet)
            {
                if (!ValidateTarget(ctx, leftTarget) || !ValidateTarget(ctx, rightTarget))
                {
                    return result;
                }

                // Both targets resolve against the pose before either foot is solved
                var leftTargetTransform = GetFootTargetTransform(ctx, leftTarget);
                var rightTargetTransform = GetFootTargetTransform(ctx, rightTarget);

                TwoBoneSolver.Solve(solvePose, leftFootBoneIdx, leftTargetTransform, 0f, BlendMode, blendWeight);
                TwoBoneSolver.Solve(solvePose, rightFootBoneIdx, rightTargetTransform, 0f, BlendMode, blendWeight);
            }
            else if (leftTarget.IsSet || rightTarget.IsSet)
            {
                var target = leftTarget.IsSet ? leftTarget : rightTarget;
                if (!ValidateTarget(ctx, target))
                {
                    return result;
                }

                // A single foot runs the two bone IK task, which converts world targets the other way round
                target.TryGetTransform(solvePose, out var targetTransform);
                if (!target.IsBoneTarget && IsTargetInWorldSpace)
                {
                    targetTransform *= ctx.WorldTransformInverse;
                }

                TwoBoneSolver.Solve(solvePose, leftTarget.IsSet ? leftFootBoneIdx : rightFootBoneIdx, targetTransform, 0f, BlendMode, blendWeight);
            }
            else
            {
                return result;
            }

            solvePose.CopyParentSpaceTransformsTo(PoseTransforms);
            result.Pose = PoseTransforms;
            return result;
        }

        // The target that leaves the effector where the animation put it
        Transform GetEffectorHint(GraphContext ctx, int boneIdx)
        {
            var effector = solvePose.GetModelSpaceTransform(boneIdx);
            return IsTargetInWorldSpace ? effector * ctx.WorldTransform : effector;
        }

        Transform GetFootTargetTransform(GraphContext ctx, Target target)
        {
            target.TryGetTransform(solvePose, out var targetTransform);

            if (!target.IsBoneTarget && IsTargetInWorldSpace)
            {
                targetTransform = ctx.WorldTransformInverse * targetTransform;
            }

            return targetTransform;
        }
    }
}
