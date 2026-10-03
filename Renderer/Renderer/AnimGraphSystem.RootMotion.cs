using System.Diagnostics;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    enum BlendState : byte
    {
        None = 0,
        BlendingIn,
        BlendingOut,
        FullyIn,
        FullyOut = None,
    }

    // Overrides the child's root motion with a desired moving velocity and facing direction, optionally
    // only while root motion events are sampled.
    partial class RootMotionOverrideNode
    {
        enum OverrideFlag
        {
            AllowMoveX,
            AllowMoveY,
            AllowMoveZ,
            AllowFacingPitch,
            ListenForEvents,
        }

        VectorValueNode? DesiredMovingVelocityNode;
        VectorValueNode? DesiredFacingDirectionNode;
        FloatValueNode? LinearVelocityLimitNode;
        FloatValueNode? AngularVelocityLimitNode;
        BoolValueNode? EnabledNode;

        float blendTime;
        float desiredBlendDuration;
        BlendState blendState = BlendState.None;
        bool isFirstUpdate;

        bool IsFlagSet(OverrideFlag flag) => OverrideFlags.IsFlagSet(1u << (int)flag);

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(DesiredMovingVelocityNodeIdx, ref DesiredMovingVelocityNode);
            ctx.SetOptionalNodeFromIndex(DesiredFacingDirectionNodeIdx, ref DesiredFacingDirectionNode);
            ctx.SetOptionalNodeFromIndex(LinearVelocityLimitNodeIdx, ref LinearVelocityLimitNode);
            ctx.SetOptionalNodeFromIndex(AngularVelocityLimitNodeIdx, ref AngularVelocityLimitNode);
            ctx.SetOptionalNodeFromIndex(EnabledNodeIdx, ref EnabledNode);
            base.Instantiate(ctx);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            DesiredMovingVelocityNode?.Initialize(ctx);
            DesiredFacingDirectionNode?.Initialize(ctx);
            AngularVelocityLimitNode?.Initialize(ctx);
            LinearVelocityLimitNode?.Initialize(ctx);
            EnabledNode?.Initialize(ctx);

            blendTime = 0f;
            desiredBlendDuration = 0f;
            blendState = BlendState.None;
            isFirstUpdate = true;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            EnabledNode?.Shutdown(ctx);
            DesiredMovingVelocityNode?.Shutdown(ctx);
            DesiredFacingDirectionNode?.Shutdown(ctx);
            AngularVelocityLimitNode?.Shutdown(ctx);
            LinearVelocityLimitNode?.Shutdown(ctx);

            base.ShutdownInternal(ctx);
        }

        // Uses root motion events to calculate the override weight: 0 keeps the original root motion,
        // 1 fully overrides it
        float CalculateOverrideWeight(GraphContext ctx)
        {
            Debug.Assert(IsFlagSet(OverrideFlag.ListenForEvents));

            var sampledEventIdx = -1;

            for (var i = 0; i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || !sampledEvent.IsFromActiveBranch || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                if (sampledEvent.AnimEvent?.ClassName != "CNmRootMotionEvent")
                {
                    continue;
                }

                // If we are not blending, find the event with the greatest weight to determine the blend duration
                if (blendState == BlendState.None)
                {
                    if (sampledEventIdx == -1 || sampledEvent.Weight > ctx.SampledEvents[sampledEventIdx].Weight)
                    {
                        sampledEventIdx = i;
                    }
                }
                else // Just checking if we still have an event or not
                {
                    sampledEventIdx = i;
                    break;
                }
            }

            // Update blend state
            if (sampledEventIdx != -1)
            {
                var eventBlendTime = ctx.SampledEvents[sampledEventIdx].AnimEvent!.Data.GetFloatProperty("m_flBlendTimeSeconds");

                // Start blend in
                if (blendState == BlendState.FullyOut)
                {
                    desiredBlendDuration = eventBlendTime;
                    Debug.Assert(desiredBlendDuration >= 0f);

                    // If we have an event on the first update, then skip the blend
                    if (isFirstUpdate)
                    {
                        blendState = BlendState.FullyIn;
                        blendTime = desiredBlendDuration;
                    }
                    else
                    {
                        blendState = BlendState.BlendingIn;
                        blendTime = 0f;
                    }
                }
                // Cancel blend out
                else if (blendState == BlendState.BlendingOut)
                {
                    var currentPercentageThroughBlend = blendTime / desiredBlendDuration;

                    blendState = BlendState.BlendingIn;
                    desiredBlendDuration = eventBlendTime;
                    blendTime = currentPercentageThroughBlend * desiredBlendDuration;

                    Debug.Assert(desiredBlendDuration >= 0f);
                }
            }
            else // No event
            {
                // Start blend out
                if (blendState == BlendState.FullyIn)
                {
                    blendState = BlendState.BlendingOut;
                    blendTime = 0f;
                }
                // Cancel blend in
                else if (blendState == BlendState.BlendingIn)
                {
                    blendState = BlendState.BlendingOut;
                    blendTime = desiredBlendDuration - blendTime;
                }
            }

            // Update blend
            if (blendState is BlendState.BlendingIn or BlendState.BlendingOut)
            {
                blendTime += ctx.DeltaTime;
                if (blendTime > desiredBlendDuration)
                {
                    blendTime = desiredBlendDuration;
                    blendState = blendState == BlendState.BlendingIn ? BlendState.FullyIn : BlendState.FullyOut;
                }
            }

            return blendState switch
            {
                BlendState.FullyOut => 1f,
                BlendState.BlendingIn => 1f - (blendTime / desiredBlendDuration),
                BlendState.BlendingOut => blendTime / desiredBlendDuration,
                _ => 0f,
            };
        }

        void ModifyRootMotion(GraphContext ctx, ref GraphPoseNodeResult nodeResult)
        {
            var adjustedDisplacementDelta = nodeResult.RootMotionDelta;

            // Move
            var isMoveModificationAllowed = DesiredMovingVelocityNode != null
                && (IsFlagSet(OverrideFlag.AllowMoveX) || IsFlagSet(OverrideFlag.AllowMoveY) || IsFlagSet(OverrideFlag.AllowMoveZ));

            if (isMoveModificationAllowed)
            {
                var desiredMovingVelocity = DesiredMovingVelocityNode!.GetValue(ctx);

                // Override the requested axes with the desired move
                var translation = nodeResult.RootMotionDelta.Position;
                translation.X = IsFlagSet(OverrideFlag.AllowMoveX) ? desiredMovingVelocity.X * ctx.DeltaTime : translation.X;
                translation.Y = IsFlagSet(OverrideFlag.AllowMoveY) ? desiredMovingVelocity.Y * ctx.DeltaTime : translation.Y;
                translation.Z = IsFlagSet(OverrideFlag.AllowMoveZ) ? desiredMovingVelocity.Z * ctx.DeltaTime : translation.Z;

                // Apply max linear velocity limit
                var maxLinearVelocity = MaxLinearVelocity;
                if (LinearVelocityLimitNode != null)
                {
                    maxLinearVelocity = MathF.Abs(LinearVelocityLimitNode.GetValue(ctx));
                }

                if (maxLinearVelocity >= 0f)
                {
                    var maxLinearValue = ctx.DeltaTime * maxLinearVelocity;
                    if (translation.LengthSquared() > maxLinearValue * maxLinearValue)
                    {
                        translation = Vector3.Normalize(translation) * maxLinearValue;
                    }
                }

                adjustedDisplacementDelta.Position = translation;
            }

            // Facing
            if (DesiredFacingDirectionNode != null)
            {
                var desiredFacingCS = DesiredFacingDirectionNode.GetValue(ctx);

                // Remove pitch facing if this is not allowed
                if (!IsFlagSet(OverrideFlag.AllowFacingPitch))
                {
                    desiredFacingCS.Z = 0f;
                }

                if (!TransformMath.IsNearZero(desiredFacingCS))
                {
                    desiredFacingCS = Vector3.Normalize(desiredFacingCS);

                    // The total delta rotation between our current facing and the desired facing
                    var deltaRotation = TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, desiredFacingCS, TransformMath.WorldUp);

                    // Apply max angular velocity limit
                    var maxAngularVelocity = MaxAngularVelocityRadians;
                    if (AngularVelocityLimitNode != null)
                    {
                        maxAngularVelocity = MathF.Abs(float.DegreesToRadians(AngularVelocityLimitNode.GetValue(ctx)));
                    }

                    if (maxAngularVelocity >= 0f)
                    {
                        var maxAngularValue = ctx.DeltaTime * maxAngularVelocity;
                        var desiredRotationAngle = TransformMath.GetAngle(deltaRotation);
                        if (desiredRotationAngle > maxAngularValue)
                        {
                            var t = 1f - ((desiredRotationAngle - maxAngularValue) / desiredRotationAngle);
                            deltaRotation = Quaternion.Slerp(Quaternion.Identity, deltaRotation, t);
                        }
                    }

                    adjustedDisplacementDelta.Angle = deltaRotation;
                }
            }

            // Perform modification
            var overrideWeight = IsFlagSet(OverrideFlag.ListenForEvents) ? CalculateOverrideWeight(ctx) : 1f;

            if (overrideWeight == 1f)
            {
                nodeResult.RootMotionDelta = adjustedDisplacementDelta;
            }
            else if (overrideWeight > 0f)
            {
                nodeResult.RootMotionDelta = Blender.BlendRootMotion(nodeResult.RootMotionDelta, adjustedDisplacementDelta, overrideWeight);
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx, updateRange);

            // Valve addition: an optional enable input bypasses the override
            if (EnabledNode == null || EnabledNode.GetValue(ctx))
            {
                // Always modify root motion even if the child is invalid
                ModifyRootMotion(ctx, ref result);
            }

            isFirstUpdate = false;
            return result;
        }
    }
}
