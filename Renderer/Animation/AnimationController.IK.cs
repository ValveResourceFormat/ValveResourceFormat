using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        private float[] constrainedFlexValues = [];

        /// <summary>Gets or sets whether first-person legs mode is enabled (zeros bones from spine_0 and up, keeping the pelvis and legs).</summary>
        internal bool EnableFirstPersonLegs { get; set; }

        /// <summary>
        /// Gets the controller this one is bone merged onto. While set, <see cref="Update"/> poses each bone
        /// from the bone of the same name in that controller's <see cref="FullPose"/> instead of animating.
        /// </summary>
        internal AnimationController? BoneMergeParent { get; private set; }

        // The bone of the parent each bone takes its pose from, or -1 for one that keeps its bind offset from its
        // own parent bone. Null when the skeletons match bone for bone, so the pose is copied whole.
        private int[]? boneMergeMap;

        /// <summary>
        /// Bone merges this controller onto <paramref name="parent"/>, matching bones by name.
        /// </summary>
        /// <param name="parent">The controller to take the pose from.</param>
        internal void SetBoneMergeParent(AnimationController parent)
        {
            BoneMergeParent = parent;

            var bones = Skeleton.Bones;
            var parentBones = parent.Skeleton.Bones;
            var map = new int[bones.Length];
            var matchesWhole = bones.Length == parentBones.Length;

            for (var i = 0; i < bones.Length; i++)
            {
                map[i] = parent.Skeleton.GetBoneIndex(bones[i].Name);
                matchesWhole &= map[i] == i;
            }

            boneMergeMap = matchesWhole ? null : map;
        }

        private void UpdateBoneMerge(AnimationController parent)
        {
            var parentPose = parent.FullPose;

            if (boneMergeMap == null)
            {
                parentPose.CopyTo(Pose, 0);
                return;
            }

            foreach (var root in Skeleton.Roots)
            {
                MergeSubtree(root, Matrix4x4.Identity, parentPose);
            }
        }

        private void MergeSubtree(Bone bone, Matrix4x4 parentTransform, Matrix4x4[] parentPose)
        {
            var transform = boneMergeMap![bone.Index] is >= 0 and var source ? parentPose[source] : bone.BindPose * parentTransform;

            Pose[bone.Index] = transform;

            foreach (var child in bone.Children)
            {
                MergeSubtree(child, transform, parentPose);
            }
        }

        private Matrix4x4[]? fullPose;
        private int? spine0BoneIndex;

        /// <summary>
        /// Gets the pose as it was before <see cref="EnableFirstPersonLegs"/> hid the upper body, which is
        /// <see cref="Pose"/> itself when nothing is hidden. Bone merged controllers take this one.
        /// </summary>
        internal Matrix4x4[] FullPose => EnableFirstPersonLegs && fullPose != null ? fullPose : Pose;

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

            fullPose ??= new Matrix4x4[Pose.Length];
            Pose.CopyTo(fullPose, 0);

            spine0BoneIndex ??= Skeleton.GetBoneIndex("spine_0");

            var spine0 = spine0BoneIndex.Value;
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
