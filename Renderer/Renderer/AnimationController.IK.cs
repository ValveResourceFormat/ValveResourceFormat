using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        public void ApplyFirstpersonLegs()
        {
            var spine0 = Skeleton.GetBoneIndex("spine_0");
            if (spine0 != -1)
            {
                ZeroBoneAndChildren(Pose, Skeleton.Bones[spine0]);
            }

            // var time = DateTime.Now;
            // var blink = (int)(time.Ticks / TimeSpan.TicksPerSecond) % 2 == 0;
            // if (blink)
            // {
            //     return;
            // }
        }

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

            EvaluateViewmodelConstraints(skeleton, pose);
        }

        private static void EvaluateViewmodelConstraints(Skeleton skeleton, Span<Matrix4x4> pose)
        {
            Span<(string Target, string Twist, string Twist1, float Side)> constraints =
            [
                ("hand_r", "arm_lower_r_twist", "arm_lower_r_twist1", 1.0f),
                ("arm_lower_r", "arm_upper_r_twist", "arm_upper_r_twist1", 1.0f),

                ("hand_l", "arm_lower_l_twist", "arm_lower_l_twist1", -0.4f),
                ("arm_lower_l", "arm_upper_l_twist", "arm_upper_l_twist1", -0.4f),
            ];

            foreach (var constraint in constraints)
            {
                var target = skeleton[constraint.Target];
                var twist = skeleton[constraint.Twist];
                var twist1 = skeleton[constraint.Twist1];

                if (target != null && twist1 != null)
                {
                    ApplyTwistIK(pose, target, twist, twist1, constraint.Side);
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

        private static void ApplyTwistIK(Span<Matrix4x4> pose, Bone hand, Bone? twist, Bone twist1, float side)
        {
            // Extract hand rotation and calculate twist rotation
            Matrix4x4.Decompose(pose[hand.Index], out _, out var handRotation, out _);
            var handEuler = Vector3.RadiansToDegrees(QuaternionToEuler(handRotation)); // ModelExtract.ToEulerAngles(handRotation);
            var handTwist = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(handEuler.X - 65f) * side);
            handTwist = Quaternion.Slerp(Quaternion.Identity, handTwist, 1.0f);

            // Rotate in local space
            Matrix4x4.Decompose(pose[twist1.Index], out var scale, out var rotation, out var translation);
            pose[twist1.Index] = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(rotation * handTwist)
                * Matrix4x4.CreateTranslation(translation);

            // Apply to twist bone
            Matrix4x4.Decompose(pose[twist1.Index], out scale, out rotation, out translation);

            if (twist != null)
            {
                pose[twist.Index] = Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromQuaternion(rotation * handTwist)
                    * Matrix4x4.CreateTranslation(translation);
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
