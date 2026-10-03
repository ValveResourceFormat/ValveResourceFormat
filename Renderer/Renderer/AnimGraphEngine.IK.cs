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
                    ctx.LogWarning(NodeIdx, $"Invalid input target bone ID ('{effectorTarget.BoneID}') specified");
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
    }
}
