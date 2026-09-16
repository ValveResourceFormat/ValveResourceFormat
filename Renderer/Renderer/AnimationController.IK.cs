using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        private float[] constrainedFlexValues = [];

        /// <summary>Gets or sets whether first-person legs mode is enabled (zeros bones from spine_0 and up, keeping the pelvis and legs).</summary>
        internal bool EnableFirstPersonLegs { get; set; }

        /// <summary>Gets or sets whether the bone constraints are evaluated after each pose update.</summary>
        public bool EnableConstraints { get; set; } = true;

        /// <summary>Gets or sets the bone constraints evaluated on the pose after each update.</summary>
        public BoneConstraintSolver? BoneConstraints { get; set; }

        /// <summary>
        /// Gets the flex controller values of the current frame with the morph driving constraints applied,
        /// or the frame's own values when no constraint writes morphs.
        /// </summary>
        public float[]? FlexValues => BoneConstraints is { WritesMorphs: true } && AnimationFrame != null
            ? constrainedFlexValues
            : AnimationFrame?.Datas;

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
        /// Evaluates the bone constraints on the current pose, and the morph driving ones on the frame's flex controller values.
        /// </summary>
        public void ApplyConstraints()
        {
            if (BoneConstraints is not { Count: > 0 } || !EnableConstraints)
            {
                return;
            }

            var datas = AnimationFrame?.Datas ?? [];
            var length = Math.Max(datas.Length, flexControllers.Length);

            if (constrainedFlexValues.Length != length)
            {
                constrainedFlexValues = new float[length];
            }

            Array.Clear(constrainedFlexValues);
            datas.CopyTo(constrainedFlexValues, 0);

            BoneConstraints.Evaluate(Pose, constrainedFlexValues);
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
    }
}
