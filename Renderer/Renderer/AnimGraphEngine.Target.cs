using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class TargetValueNode
    {
        Target cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public Target GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual Target GetValueInternal(GraphContext ctx) => throw new NotImplementedException();
    }

    partial class CachedTargetNode
    {
        TargetValueNode InputValueNode;
        Target CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            if (!HasCachedValue)
            {
                Debug.Assert(Mode == CachedValueMode.OnExit);

                if (ctx.BranchState == BranchState.Inactive)
                {
                    HasCachedValue = true;
                }
                else
                {
                    CachedValue = InputValueNode.GetValue(ctx);
                }
            }

            return CachedValue;
        }
    }

    partial class ConstTargetNode
    {
        protected override Target GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterTargetNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            // Stored as a FrameBone transform (set externally / from the UI); exposed as a set, non-bone target.
            return new Target(ctx.Controller.TargetParameters[parameterName]);
        }
    }

    // Valve-specific (not present in Esoterica): picks the option target that best matches a reference
    // (parameter) target, scored by weighted position distance + orientation difference.
    partial class TargetSelectorNode
    {
        TargetValueNode[] OptionNodes;
        TargetValueNode? ParameterNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetOptionalNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            if (OptionNodes.Length == 0)
            {
                return default;
            }

            Transform reference = default;
            var hasReference = ParameterNode != null && TryResolve(ctx, ParameterNode.GetValue(ctx), out reference);

            var bestCost = float.MaxValue;
            var selected = false;
            Target bestTarget = default;

            for (var i = 0; i < OptionNodes.Length; i++)
            {
                var option = OptionNodes[i].GetValue(ctx);
                if (!option.IsSet && IgnoreInvalidOptions)
                {
                    continue;
                }

                // With no reference target to score against, just return the first usable option.
                if (!hasReference)
                {
                    return option;
                }

                if (!TryResolve(ctx, option, out var optionTransform))
                {
                    continue;
                }

                // Lower cost = better match.
                var positionCost = Vector3.Distance(optionTransform.Position, reference.Position);
                var orientationCost = QuaternionAngle(optionTransform.Rotation, reference.Rotation);
                var cost = (PositionScoreWeight * positionCost) + (OrientationScoreWeight * orientationCost);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestTarget = option;
                    selected = true;
                }
            }

            if (!selected)
            {
                ctx.LogWarning(NodeIdx, "TargetSelector failed to select a valid option.");
                return default;
            }

            return bestTarget;
        }

        bool TryResolve(GraphContext ctx, Target target, out Transform transform)
        {
            if (!target.IsSet || !target.TryGetTransform(ctx.Pose, out transform))
            {
                transform = default;
                return false;
            }

            if (IsWorldSpaceTarget)
            {
                transform *= ctx.WorldTransformInverse;
            }

            return true;
        }

        // Angle (radians) between two orientations, ignoring sign (double-cover).
        static float QuaternionAngle(Quaternion a, Quaternion b)
        {
            var dot = Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f);
            return 2f * MathF.Acos(dot);
        }
    }

    partial class TargetOffsetNode
    {
        TargetValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override Target GetValueInternal(GraphContext ctx)
        {
            var target = InputValueNode.GetValue(ctx);

            if (target.IsSet)
            {
                if (target.IsBoneTarget)
                {
                    target.SetOffsets(RotationOffset, TranslationOffset, IsBoneSpaceOffset);
                }
                else
                {
                    ctx.LogWarning(NodeIdx, "Trying to set an offset on a transform target node - Offset are only allowed on bone targets!");
                }
            }
            else
            {
                ctx.LogWarning(NodeIdx, "Trying to set an offset on an unset node!");
            }

            return target;
        }
    }

    partial class VirtualParameterTargetNode : TargetValueNode
    {
        TargetValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the TargetValueNode base.
        protected override Target GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
