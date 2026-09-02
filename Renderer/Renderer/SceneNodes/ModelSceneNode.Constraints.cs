using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// The bone constraints this node simulates: the twist constraints the animation controller solves,
    /// and the morphs driven by where one bone points relative to another.
    /// </summary>
    public partial class ModelSceneNode
    {
        private DotToMorphConstraint[] dotToMorphConstraints = [];

        private float[] dotToMorphValues = [];

        /// <summary>
        /// Parses the constraints that drive a morph from a bone's facing, resolving the bones and the
        /// flex controller they name so the update does not have to search per frame.
        /// </summary>
        protected static DotToMorphConstraint[] ParseDotToMorphConstraints(Model model)
        {
            var bones = model.Skeleton.Bones;
            var controllers = model.FlexControllers;
            var constraints = new List<DotToMorphConstraint>();

            foreach (var constraintData in model.GetBoneConstraints("CBoneConstraintDotToMorph"))
            {
                var remap = constraintData.GetFloatArray("m_flRemap");
                if (remap == null || remap.Length < 4)
                {
                    continue;
                }

                var boneName = constraintData.GetStringProperty("m_sBoneName");
                var targetName = constraintData.GetStringProperty("m_sTargetBoneName");
                var channel = constraintData.GetStringProperty("m_sMorphChannelName");

                var constraint = new DotToMorphConstraint
                {
                    BoneName = boneName,
                    TargetBoneName = targetName,
                    MorphChannelName = channel,
                    InputMin = remap[0],
                    InputMax = remap[1],
                    OutputMin = remap[2],
                    OutputMax = remap[3],
                    BoneIndex = Array.FindIndex(bones, b => b.Name == boneName),
                    TargetBoneIndex = Array.FindIndex(bones, b => b.Name == targetName),
                    MorphChannelIndex = Array.FindIndex(controllers, c => c.Name == channel),
                };

                if (constraint.BoneIndex >= 0 && constraint.TargetBoneIndex >= 0 && constraint.MorphChannelIndex >= 0)
                {
                    constraints.Add(constraint);
                }
            }

            return [.. constraints];
        }

        /// <summary>
        /// Applies the bone driven morphs on top of the animated controller values.
        /// </summary>
        private void ApplyDotToMorphConstraints(DotToMorphConstraint[] constraints, float[] controllerValues)
        {
            var pose = AnimationController.Pose;

            foreach (var constraint in constraints)
            {
                if (constraint.MorphChannelIndex >= controllerValues.Length)
                {
                    continue;
                }

                var bone = pose[constraint.BoneIndex];
                var target = pose[constraint.TargetBoneIndex];

                // Measured against the bone's down axis: level with the target it reads a right angle,
                // which is what both remaps start from, and looking down opens the angle further.
                var facing = Vector3.Normalize(new Vector3(-bone.M31, -bone.M32, -bone.M33));
                var toTarget = target.Translation - bone.Translation;

                if (toTarget.LengthSquared() < 1e-12f)
                {
                    continue;
                }

                var dot = Math.Clamp(Vector3.Dot(facing, Vector3.Normalize(toTarget)), -1f, 1f);
                var degrees = MathF.Acos(dot) * (180f / MathF.PI);

                controllerValues[constraint.MorphChannelIndex] = MathUtils.RemapValClamped(
                    degrees, constraint.InputMin, constraint.InputMax, constraint.OutputMin, constraint.OutputMax);
            }
        }
    }
}
