using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        /// <summary>
        /// Applies tilt-twist constraints configured in the controller to the current pose.
        /// </summary>
        public void ApplyConstraints()
        {
            if (TwistConstraints.Length == 0)
            {
                return;
            }

            var skeleton = Skeleton;
            var pose = Pose.AsSpan();

            foreach (var constraint in TwistConstraints)
            {
                EvaluateTiltTwistConstraint(constraint, skeleton, pose);
            }
        }

        private readonly struct ConstraintEvaluation
        {
            public Quaternion TwistRotation { get; init; }
            public float TwistAngleDegrees { get; init; }
        }

        private static ConstraintEvaluation EvaluateTiltTwistConstraint(TiltTwistConstraint constraint, Skeleton skeleton, Span<Matrix4x4> pose)
        {
            var targetAxis = GetAxisVector(constraint.TargetAxis);
            var slaveAxis = GetAxisVector(constraint.SlaveAxis);
            var accumulatedTwist = Quaternion.Identity;
            var totalWeight = 0f;

            foreach (var target in constraint.Targets)
            {
                var bone = skeleton[target.BoneHash];
                if (bone == null || MathF.Abs(target.Weight) < 1e-6f)
                {
                    continue;
                }

                var localRotation = GetLocalRotation(bone, pose);
                var bindRotation = bone.Angle;
                var deltaRotation = Quaternion.Multiply(Quaternion.Inverse(bindRotation), localRotation);
                deltaRotation = Quaternion.Multiply(target.Offset, deltaRotation);
                var twist = DecomposeTwistRotation(deltaRotation, targetAxis);

                if (totalWeight == 0f)
                {
                    accumulatedTwist = Quaternion.Slerp(Quaternion.Identity, twist, target.Weight);
                }
                else
                {
                    var newWeight = target.Weight / (totalWeight + target.Weight);
                    accumulatedTwist = Quaternion.Slerp(accumulatedTwist, twist, newWeight);
                }

                totalWeight += target.Weight;
            }

            var twistAngle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(accumulatedTwist.W), 0f, 1f));
            var twistAngleDegrees = twistAngle * 180f / MathF.PI;

            var slaveTwist = accumulatedTwist;
            if (constraint.TargetAxis != constraint.SlaveAxis)
            {
                slaveTwist = TransformTwistBetweenAxes(accumulatedTwist, targetAxis, slaveAxis);
            }

            foreach (var slave in constraint.Slaves)
            {
                var bone = skeleton[slave.BoneHash];
                if (bone == null || MathF.Abs(slave.Weight) < 1e-6f)
                {
                    continue;
                }

                Matrix4x4.Decompose(pose[bone.Index], out var scale, out var rotation, out var translation);

                var absWeight = MathF.Abs(slave.Weight);
                var twistToApply = slave.Weight < 0f ? Quaternion.Inverse(slaveTwist) : slaveTwist;

                Quaternion weightedTwist;
                if (MathF.Abs(absWeight - 1f) < 1e-6f)
                {
                    weightedTwist = twistToApply;
                }
                else
                {
                    weightedTwist = Quaternion.Slerp(Quaternion.Identity, twistToApply, absWeight);
                }

                var newRotation = Quaternion.Multiply(weightedTwist, rotation);

                pose[bone.Index] = Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromQuaternion(newRotation)
                    * Matrix4x4.CreateTranslation(translation);
            }

            return new ConstraintEvaluation
            {
                TwistRotation = accumulatedTwist,
                TwistAngleDegrees = twistAngleDegrees
            };
        }

        private static Quaternion TransformTwistBetweenAxes(Quaternion twist, Vector3 fromAxis, Vector3 toAxis)
        {
            var angle = 2f * MathF.Acos(Math.Clamp(twist.W, -1f, 1f));
            var axisVector = new Vector3(twist.X, twist.Y, twist.Z);
            if (Vector3.Dot(axisVector, fromAxis) < 0f)
            {
                angle = -angle;
            }

            return Quaternion.CreateFromAxisAngle(toAxis, angle);
        }



        private static Quaternion DecomposeTwistRotation(Quaternion rotation, Vector3 axis)
        {
            var rotationAxis = new Vector3(rotation.X, rotation.Y, rotation.Z);

            if (rotationAxis.LengthSquared() < 1e-6f)
            {
                return Quaternion.Identity;
            }

            var projection = Vector3.Dot(rotationAxis, axis) * axis;
            var twist = new Quaternion(projection.X, projection.Y, projection.Z, rotation.W);
            var length = twist.Length();

            if (length < 1e-6f)
            {
                return Quaternion.Identity;
            }

            return Quaternion.Normalize(twist);
        }

        private static Vector3 GetAxisVector(int axisIndex)
        {
            return axisIndex switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                2 => Vector3.UnitZ,
                _ => Vector3.UnitX,
            };
        }
    }
}
