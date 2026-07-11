using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        /// <summary>
        /// Gets or sets the tilt-twist skeleton constraints. These are not yet evaluated at runtime;
        /// only hardcoded first-person viewmodel constraints are currently applied (see <see cref="ApplyConstraints"/>).
        /// </summary>
        public TiltTwistConstraint[] TwistConstraints { get; set; } = [];

        /// <summary>Gets or sets whether first-person legs mode is enabled (zeros bones from pelvis and up).</summary>
        internal bool EnableFirstPersonLegs { get; set; }

        /// <summary>Gets or sets whether viewmodel-specific twist constraints should be applied.</summary>
        internal bool EnableFirstPersonConstraints { get; set; }

        /// <summary>
        /// Applies inverse kinematics to the current pose.
        /// </summary>
        public void ApplyInverseKinematics()
        {
            ApplyConstraints();
            ApplyFirstpersonLegs();
        }

        /// <summary>
        /// Hides bones from spine_0 and up (keeping the pelvis and legs visible).
        /// </summary>
        public void ApplyFirstpersonLegs()
        {
            if (!EnableFirstPersonLegs)
            {
                return;
            }

            var spine0 = Skeleton.GetBoneIndex("spine_0");
            if (spine0 != -1)
            {
                ZeroBoneAndChildren(Pose, Skeleton.Bones[spine0]);
            }
        }

        /// <summary>
        /// Applies twist constraints to the current pose. Currently only hardcoded first-person
        /// viewmodel arm-twist constraints are applied (when <see cref="EnableFirstPersonConstraints"/>
        /// is set); the configured <see cref="TwistConstraints"/> array is not yet evaluated.
        /// </summary>
        public void ApplyConstraints()
        {
            if (TwistConstraints.Length == 0)
            {
                return;
            }

            // todo: evaluate model constraints dynamically

            if (EnableFirstPersonConstraints)
            {
                var skeleton = Skeleton;
                var pose = Pose.AsSpan();

                EvaluateViewmodelConstraints(skeleton, pose);
            }
        }

        private static void EvaluateViewmodelConstraints(Skeleton skeleton, Span<Matrix4x4> pose)
        {
            Span<(string Target, string Twist, string Twist1)> constraints =
            [
                ("hand_r", "arm_lower_r_twist", "arm_lower_r_twist1"),
                //("arm_lower_r", "arm_upper_r_twist", "arm_upper_r_twist1"),

                ("hand_l", "arm_lower_l_twist", "arm_lower_l_twist1"),
                //("arm_lower_l", "arm_upper_l_twist", "arm_upper_l_twist1"),
            ];

            foreach (var constraint in constraints)
            {
                var target = skeleton[constraint.Target];
                var twist = skeleton[constraint.Twist];
                var twist1 = skeleton[constraint.Twist1];

                if (target != null && twist1 != null)
                {
                    ApplyTwistIK(pose, target, twist, twist1);
                }
            }
        }

        private static void ZeroBoneAndChildren(Span<Matrix4x4> pose, Bone bone)
        {
            // Collapse bone to parent's transform and scale to zero to hide it
            if (bone.Parent != null)
            {
                pose[bone.Index] = Matrix4x4.CreateScale(0f) * pose[bone.Parent.Index];
            }
            else
            {
                pose[bone.Index] = Matrix4x4.CreateScale(0f);
            }

            foreach (var child in bone.Children)
            {
                ZeroBoneAndChildren(pose, child);
            }
        }

        private static void ApplyTwistIK(Span<Matrix4x4> pose, Bone hand, Bone? twist, Bone twist1)
        {
            if (hand.Parent == null)
            {
                return;
            }

            // Extract hand local rotation and calculate twist rotation
            Matrix4x4.Invert(pose[hand.Parent.Index], out var handParentInverse);
            var handLocal = pose[hand.Index] * handParentInverse;
            Matrix4x4.Decompose(handLocal, out _, out var handRotation, out _);
            var handEulerRad = QuaternionToEuler(handRotation);
            var handTwist = Quaternion.CreateFromAxisAngle(Vector3.UnitX, handEulerRad.X - 1.45f);
            handTwist = Quaternion.Slerp(Quaternion.Identity, handTwist, 1f);
            var handTwistMatrix = Matrix4x4.CreateFromQuaternion(handTwist);

            // Apply in local space: local = world * inv(parent), new world = (local * twist) * parent
            if (twist1.Parent != null)
            {
                Matrix4x4.Invert(pose[twist1.Parent.Index], out var parentInverse);
                var local = pose[twist1.Index] * parentInverse;
                pose[twist1.Index] = local * handTwistMatrix * pose[twist1.Parent.Index];
            }

            if (twist != null && twist.Parent != null)
            {
                Matrix4x4.Invert(pose[twist.Parent.Index], out var parentInverse);
                var local = pose[twist.Index] * parentInverse;
                pose[twist.Index] = local * handTwistMatrix * pose[twist.Parent.Index];
            }
        }

        private static Vector3 QuaternionToEuler(Quaternion q)
        {
            var euler = new Vector3();

            // Roll (x-axis rotation)
            var sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            var cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            euler.X = MathF.Atan2(sinr_cosp, cosr_cosp);

            // Pitch (y-axis rotation)
            var sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (MathF.Abs(sinp) >= 1)
            {
                euler.Y = MathF.CopySign(MathF.PI / 2, sinp);
            }
            else
            {
                euler.Y = MathF.Asin(sinp);
            }

            // Yaw (z-axis rotation)
            var siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            var cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            euler.Z = MathF.Atan2(siny_cosp, cosy_cosp);

            return euler;
        }
    }
}
