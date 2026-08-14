using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class VectorValueNode
    {
        Vector3 cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public Vector3 GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual Vector3 GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return Vector3.Zero;
        }
    }

    partial class CachedVectorNode
    {
        VectorValueNode InputValueNode;
        Vector3 CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            if (!HasCachedValue)
            {
                // OnEntry captures on first evaluation and holds (Esoterica captures at node
                // initialization; per-state-entry recapture needs the activation lifecycle).
                if (Mode == CachedValueMode.OnEntry)
                {
                    CachedValue = InputValueNode.GetValue(ctx);
                    HasCachedValue = true;
                }
                else if (ctx.BranchState == BranchState.Inactive)
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

    partial class ConstVectorNode
    {
        protected override Vector3 GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterVectorNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            return ctx.Graph.VectorParameters[parameterName].AsVector3();
        }
    }

    // A virtual parameter is a graph-computed sub-expression: evaluates its child (cached once per update).
    partial class VirtualParameterVectorNode
    {
        VectorValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the VectorValueNode base.
        protected override Vector3 GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }

    // Extracts a float from a vector input: a component, the length, or a character-space angle
    // (Esoterica VectorInfoNode; vectors are assumed to be in character space, forward = -Y).
    partial class VectorInfoNode
    {
        VectorValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override float GetValueInternal(GraphContext ctx)
        {
            var inputVector = InputValueNode.GetValue(ctx);

            switch (DesiredInfo)
            {
                case VectorInfoNode__Info.X:
                    return inputVector.X;

                case VectorInfoNode__Info.Y:
                    return inputVector.Y;

                case VectorInfoNode__Info.Z:
                    return inputVector.Z;

                case VectorInfoNode__Info.Length:
                    return inputVector.Length();

                case VectorInfoNode__Info.AngleHorizontal:
                {
                    if (inputVector.LengthSquared() > 1e-8f)
                    {
                        return CalculateYawAngleDegrees(WorldForward, Vector3.Normalize(inputVector));
                    }

                    ctx.LogWarning(NodeIdx, "Zero input vector for info node!");
                    return 0f;
                }

                case VectorInfoNode__Info.AngleVertical:
                {
                    if (inputVector.LengthSquared() > 1e-8f)
                    {
                        return CalculatePitchAngleDegrees(WorldForward, Vector3.Normalize(inputVector));
                    }

                    ctx.LogWarning(NodeIdx, "Zero input vector for info node!");
                    return 0f;
                }

                default:
                    return 0f;
            }
        }

        static readonly Vector3 WorldForward = new(0f, -1f, 0f);

        // Signed rotation around Z between the reference and v (Esoterica CalculateYawAngleBetweenVectors)
        static float CalculateYawAngleDegrees(Vector3 reference, Vector3 v)
        {
            var referenceLength2D = MathF.Sqrt((reference.X * reference.X) + (reference.Y * reference.Y));
            var vLength2D = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));
            if (referenceLength2D < 1e-4f || vLength2D < 1e-4f)
            {
                return 0f;
            }

            var dot2D = ((reference.X * v.X) + (reference.Y * v.Y)) / (referenceLength2D * vLength2D);
            var angle = MathF.Acos(Math.Clamp(dot2D, -1f, 1f));

            // Sign from cross(reference, v) . UnitZ
            var crossZ = (reference.X * v.Y) - (reference.Y * v.X);
            if (crossZ < 0f)
            {
                angle = -angle;
            }

            return float.RadiansToDegrees(angle);
        }

        // Elevation angle difference (Esoterica CalculatePitchAngleBetweenUnitVectors, unit inputs)
        static float CalculatePitchAngleDegrees(Vector3 reference, Vector3 v)
        {
            var vElevationAngle = MathF.Asin(Math.Clamp(v.Z, -1f, 1f));
            var referenceElevationAngle = MathF.Asin(Math.Clamp(reference.Z, -1f, 1f));
            return float.RadiansToDegrees(vElevationAngle - referenceElevationAngle);
        }
    }

    // Builds a vector from an optional vector input with optional per-component overrides.
    partial class VectorCreateNode
    {
        VectorValueNode? InputVectorValueNode;
        FloatValueNode? InputXValueNode;
        FloatValueNode? InputYValueNode;
        FloatValueNode? InputZValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(InputVectorValueNodeIdx, ref InputVectorValueNode);
            ctx.SetOptionalNodeFromIndex(InputValueXNodeIdx, ref InputXValueNode);
            ctx.SetOptionalNodeFromIndex(InputValueYNodeIdx, ref InputYValueNode);
            ctx.SetOptionalNodeFromIndex(InputValueZNodeIdx, ref InputZValueNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            var value = InputVectorValueNode?.GetValue(ctx) ?? Vector3.Zero;

            if (InputXValueNode != null)
            {
                value.X = InputXValueNode.GetValue(ctx);
            }

            if (InputYValueNode != null)
            {
                value.Y = InputYValueNode.GetValue(ctx);
            }

            if (InputZValueNode != null)
            {
                value.Z = InputZValueNode.GetValue(ctx);
            }

            return value;
        }
    }

    partial class VectorNegateNode
    {
        VectorValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx) => -InputValueNode.GetValue(ctx);
    }

    partial class TargetPointNode
    {
        TargetValueNode TargetNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref TargetNode);
        }

        protected override Vector3 GetValueInternal(GraphContext ctx)
        {
            var target = TargetNode.GetValue(ctx);
            if (!target.IsSet || !target.TryGetTransform(ctx.Pose, out var transform))
            {
                return Vector3.Zero;
            }

            if (IsWorldSpaceTarget)
            {
                transform *= ctx.WorldTransformInverse;
            }

            return transform.Position;
        }
    }
}
