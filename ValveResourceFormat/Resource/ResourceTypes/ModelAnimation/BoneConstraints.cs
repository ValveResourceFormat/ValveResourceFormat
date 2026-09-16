using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// A rule compiled into a model that drives bones or morphs from the pose of other bones
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBoneConstraintBase">CBoneConstraintBase</seealso>
    public abstract class BoneConstraintBase
    {
        /// <summary>
        /// Reads every constraint of a model that has a known class, in compiled order.
        /// </summary>
        public static BoneConstraintBase[] ReadList(Model model)
        {
            var constraints = new List<BoneConstraintBase>();

            foreach (var constraint in model.BoneConstraints)
            {
                BoneConstraintBase? parsed = constraint.ClassName switch
                {
                    "CTiltTwistConstraint" => new TiltTwistConstraint(constraint.Data),
                    "CTwistConstraint" => new TwistConstraint(constraint.Data),
                    "CAimConstraint" => new AimConstraint(constraint.Data),
                    "COrientConstraint" => new OrientConstraint(constraint.Data),
                    "CPointConstraint" => new PointConstraint(constraint.Data),
                    "CParentConstraint" => new ParentConstraint(constraint.Data),
                    "CMorphConstraint" => new MorphConstraint(constraint.Data),
                    "CBoneConstraintPoseSpaceBone" => new PoseSpaceBoneConstraint(constraint.Data),
                    "CBoneConstraintPoseSpaceMorph" => new PoseSpaceMorphConstraint(constraint.Data),
                    "CBoneConstraintDotToMorph" => new DotToMorphConstraint(constraint.Data),
                    "CBoneConstraintRbf" => new RbfConstraint(constraint.Data),
                    _ => null,
                };

                if (parsed != null)
                {
                    constraints.Add(parsed);
                }
            }

            return [.. constraints];
        }

        // A non-empty name overrides the stored hash.
        private protected static uint ReadBoneHash(KVObject data)
        {
            var name = data.GetStringProperty("m_sName", string.Empty);
            return string.IsNullOrEmpty(name) ? data.GetUInt32Property("m_nBoneHash") : StringToken.Get(name);
        }

        private protected static IReadOnlyList<KVObject> ReadObjects(KVObject data, string name)
            => data.GetArray(name) ?? [];
    }

    /// <summary>
    /// A bone driven by the constraint.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CConstraintSlave">CConstraintSlave</seealso>
    public class ConstrainedBone
    {
        /// <summary>Gets or sets the rest orientation the twist and morph constraints build on.</summary>
        public Quaternion BaseOrientation { get; set; } = Quaternion.Identity;

        /// <summary>Gets or sets the rest position the morph constraint builds on.</summary>
        public Vector3 BasePosition { get; set; }

        /// <summary>Gets or sets the hash of the bone name.</summary>
        public uint BoneHash { get; set; }

        /// <summary>Gets or sets how strongly the result is applied to this bone.</summary>
        public float Weight { get; set; }
    }

    /// <summary>
    /// A bone or attachment the constraint reads.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CConstraintTarget">CConstraintTarget</seealso>
    public class ConstraintTarget
    {
        /// <summary>Gets or sets the rotation offset applied on top of the target.</summary>
        public Quaternion Offset { get; set; } = Quaternion.Identity;

        /// <summary>Gets or sets the position offset applied on top of the target.</summary>
        public Vector3 PositionOffset { get; set; }

        /// <summary>Gets or sets the hash of the bone or attachment name.</summary>
        public uint BoneHash { get; set; }

        /// <summary>Gets or sets the weight of this target in the blend.</summary>
        public float Weight { get; set; }

        /// <summary>Gets or sets whether <see cref="BoneHash"/> names an attachment rather than a bone.</summary>
        public bool IsAttachment { get; set; }
    }

    /// <summary>
    /// A constraint that drives slave bones from weighted targets.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBaseConstraint">CBaseConstraint</seealso>
    public abstract class BaseConstraint : BoneConstraintBase
    {
        private protected BaseConstraint(KVObject data)
        {
            Name = data.GetStringProperty("m_name", string.Empty);
            UpVector = data.GetSubCollection("m_vUpVector").ToVector3();

            ConstrainedBones = [.. ReadObjects(data, "m_slaves").Select(s => new ConstrainedBone
            {
                BaseOrientation = s.GetSubCollection("m_qBaseOrientation").ToQuaternion(),
                BasePosition = s.GetSubCollection("m_vBasePosition").ToVector3(),
                BoneHash = ReadBoneHash(s),
                Weight = s.GetFloatProperty("m_flWeight"),
            })];

            Targets = [.. ReadObjects(data, "m_targets").Select(t => new ConstraintTarget
            {
                Offset = t.GetSubCollection("m_qOffset").ToQuaternion(),
                PositionOffset = t.GetSubCollection("m_vOffset").ToVector3(),
                BoneHash = ReadBoneHash(t),
                Weight = t.GetFloatProperty("m_flWeight"),
                IsAttachment = t.GetBooleanProperty("m_bIsAttachment"),
            })];
        }

        /// <summary>Gets or sets the name of the constraint.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the up vector used by the aim and twist constraints.</summary>
        public Vector3 UpVector { get; set; }

        /// <summary>Gets or sets the bones this constraint writes.</summary>
        public ConstrainedBone[] ConstrainedBones { get; set; }

        /// <summary>Gets or sets the bones and attachments this constraint reads.</summary>
        public ConstraintTarget[] Targets { get; set; }
    }

    /// <summary>
    /// Copies the twist of the first target around <see cref="TargetAxis"/> onto the first constrained bone around
    /// <see cref="ConstrainedAxis"/>, scaled by that bone's weight. CS2 arms and legs drive their twist bones with it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CTiltTwistConstraint">CTiltTwistConstraint</seealso>
    public class TiltTwistConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public TiltTwistConstraint(KVObject data) : base(data)
        {
            TargetAxis = data.GetInt32Property("m_nTargetAxis");
            ConstrainedAxis = data.GetInt32Property("m_nSlaveAxis");
        }

        /// <summary>Gets or sets the local axis (0 X, 1 Y, 2 Z) the target twist is measured around.</summary>
        public int TargetAxis { get; set; }

        /// <summary>Gets or sets the local axis (0 X, 1 Y, 2 Z) the constrained bone is rotated around.</summary>
        public int ConstrainedAxis { get; set; }
    }

    /// <summary>
    /// Distributes the twist between the second target and its parent over the constrained bones by weight.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CTwistConstraint">CTwistConstraint</seealso>
    public class TwistConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public TwistConstraint(KVObject data) : base(data)
        {
            Inverse = data.GetBooleanProperty("m_bInverse");
            ParentBindRotation = data.GetSubCollection("m_qParentBindRotation").ToQuaternion();
            ChildBindRotation = data.GetSubCollection("m_qChildBindRotation").ToQuaternion();
        }

        /// <summary>Gets or sets whether the twist is measured on the parent rather than the child.</summary>
        public bool Inverse { get; set; }

        /// <summary>Gets or sets the parent's bind rotation.</summary>
        public Quaternion ParentBindRotation { get; set; }

        /// <summary>Gets or sets the child's bind rotation.</summary>
        public Quaternion ChildBindRotation { get; set; }
    }

    /// <summary>
    /// Points the constrained bones' +Y axis at the weighted target position.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CAimConstraint">CAimConstraint</seealso>
    public class AimConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public AimConstraint(KVObject data) : base(data)
        {
            AimOffset = data.GetSubCollection("m_qAimOffset").ToQuaternion();
            UpType = data.GetInt32Property("m_nUpType");
        }

        /// <summary>Gets or sets the rotation applied after aiming.</summary>
        public Quaternion AimOffset { get; set; }

        /// <summary>
        /// Gets or sets how the up direction is found: 0 the last target's rotation applied to <see cref="BaseConstraint.UpVector"/>,
        /// 1 <see cref="BaseConstraint.UpVector"/> in model space, 2 towards the last target's position, 3 <see cref="BaseConstraint.UpVector"/> in parent space.
        /// Types 0 and 2 reserve the last target for the up direction.
        /// </summary>
        public int UpType { get; set; }
    }

    /// <summary>
    /// Rotates the constrained bones to the weighted average target rotation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/COrientConstraint">COrientConstraint</seealso>
    public class OrientConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public OrientConstraint(KVObject data) : base(data) { }
    }

    /// <summary>
    /// Moves the constrained bones to the weighted average target position.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CPointConstraint">CPointConstraint</seealso>
    public class PointConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public PointConstraint(KVObject data) : base(data) { }
    }

    /// <summary>
    /// Moves and rotates the constrained bones to the weighted average target transform.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CParentConstraint">CParentConstraint</seealso>
    public class ParentConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public ParentConstraint(KVObject data) : base(data) { }
    }

    /// <summary>
    /// Offsets one channel of the constrained bones' rest pose by a flex controller value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CMorphConstraint">CMorphConstraint</seealso>
    public class MorphConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public MorphConstraint(KVObject data) : base(data)
        {
            TargetMorph = data.GetStringProperty("m_sTargetMorph", string.Empty);
            Channel = data.GetInt32Property("m_nSlaveChannel");
            Min = data.GetFloatProperty("m_flMin");
            Max = data.GetFloatProperty("m_flMax");
        }

        /// <summary>Gets or sets the flex controller read.</summary>
        public string TargetMorph { get; set; }

        /// <summary>Gets or sets the channel written: 0-2 translate along X/Y/Z, 3-5 rotate around X/Y/Z in degrees.</summary>
        public int Channel { get; set; }

        /// <summary>Gets or sets the offset at the controller's minimum.</summary>
        public float Min { get; set; }

        /// <summary>Gets or sets the offset at the controller's maximum.</summary>
        public float Max { get; set; }
    }

    /// <summary>
    /// The radial basis function the pose space constraints interpolate their samples with.
    /// </summary>
    public enum RbfKernel
    {
        /// <summary>sqrt(1 + (r/falloff)²).</summary>
        Multiquadric = 0,
        /// <summary>1 / sqrt(1 + (r/falloff)²).</summary>
        InverseMultiquadric = 1,
        /// <summary>exp(-(r/falloff)²).</summary>
        Gaussian = 2,
        /// <summary>r.</summary>
        Linear = 3,
        /// <summary>r³.</summary>
        Cubic = 4,
        /// <summary>r⁵.</summary>
        Quintic = 5,
        /// <summary>r² log r.</summary>
        ThinPlate = 6,
    }

    /// <summary>
    /// Poses the constrained bones by interpolating authored transforms over where an attachment sits in its bone's parent space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBoneConstraintPoseSpaceBone">CBoneConstraintPoseSpaceBone</seealso>
    public class PoseSpaceBoneConstraint : BaseConstraint
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public PoseSpaceBoneConstraint(KVObject data) : base(data)
        {
            Kernel = (RbfKernel)data.GetInt32Property("m_eRbfType");
            Falloff = data.GetFloatProperty("m_flFalloff", 1f);

            Inputs = [.. ReadObjects(data, "m_inputList").Select(input => (
                input.GetSubCollection("m_inputValue").ToVector3(),
                ReadObjects(input, "m_outputTransformList").Select(t => t.ToTransform()).ToArray()))];
        }

        /// <summary>Gets or sets the interpolation kernel.</summary>
        public RbfKernel Kernel { get; set; }

        /// <summary>Gets or sets the kernel falloff distance.</summary>
        public float Falloff { get; set; }

        /// <summary>Gets or sets the samples: an input position and one local transform per constrained bone.</summary>
        public (Vector3 Value, (Vector3 Position, float Scale, Quaternion Rotation)[] Outputs)[] Inputs { get; set; }
    }

    /// <summary>
    /// Drives morphs by interpolating authored weights over where an attachment sits in its bone's parent space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBoneConstraintPoseSpaceMorph">CBoneConstraintPoseSpaceMorph</seealso>
    public class PoseSpaceMorphConstraint : BoneConstraintBase
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public PoseSpaceMorphConstraint(KVObject data)
        {
            BoneName = data.GetStringProperty("m_sBoneName", string.Empty);
            AttachmentName = data.GetStringProperty("m_sAttachmentName", string.Empty);
            OutputMorphs = data.GetArray<string>("m_outputMorph") ?? [];
            Clamp = data.GetBooleanProperty("m_bClamp");
            Kernel = (RbfKernel)data.GetInt32Property("m_eRbfType");
            Falloff = data.GetFloatProperty("m_flFalloff", 1f);

            Inputs = [.. ReadObjects(data, "m_inputList").Select(input => (
                input.GetSubCollection("m_inputValue").ToVector3(),
                input.GetFloatArray("m_outputWeightList")))];
        }

        /// <summary>Gets or sets the bone the attachment is measured in.</summary>
        public string BoneName { get; set; }

        /// <summary>Gets or sets the measured attachment.</summary>
        public string AttachmentName { get; set; }

        /// <summary>Gets or sets the flex controllers written.</summary>
        public string[] OutputMorphs { get; set; }

        /// <summary>Gets or sets whether outputs are clamped to 0-1.</summary>
        public bool Clamp { get; set; }

        /// <summary>Gets or sets the interpolation kernel.</summary>
        public RbfKernel Kernel { get; set; }

        /// <summary>Gets or sets the kernel falloff distance.</summary>
        public float Falloff { get; set; }

        /// <summary>Gets or sets the samples: an input position and one weight per output morph.</summary>
        public (Vector3 Value, float[] Weights)[] Inputs { get; set; }
    }

    /// <summary>
    /// Drives a morph from the angle between a bone's +Z axis and the direction to another bone.
    /// CS2 agents open and close the eyelids with it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBoneConstraintDotToMorph">CBoneConstraintDotToMorph</seealso>
    public class DotToMorphConstraint : BoneConstraintBase
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public DotToMorphConstraint(KVObject data)
        {
            BoneName = data.GetStringProperty("m_sBoneName", string.Empty);
            TargetBoneName = data.GetStringProperty("m_sTargetBoneName", string.Empty);
            MorphChannelName = data.GetStringProperty("m_sMorphChannelName", string.Empty);

            var remap = data.GetFloatArray("m_flRemap");
            if (remap.Length >= 4)
            {
                InputMin = remap[0];
                InputMax = remap[1];
                OutputMin = remap[2];
                OutputMax = remap[3];
            }
        }

        /// <summary>Gets or sets the bone whose facing is measured.</summary>
        public string BoneName { get; set; }

        /// <summary>Gets or sets the bone it is measured against.</summary>
        public string TargetBoneName { get; set; }

        /// <summary>Gets or sets the flex controller this drives.</summary>
        public string MorphChannelName { get; set; }

        /// <summary>Gets or sets the angle in degrees that maps to <see cref="OutputMin"/>.</summary>
        public float InputMin { get; set; }

        /// <summary>Gets or sets the angle in degrees that maps to <see cref="OutputMax"/>.</summary>
        public float InputMax { get; set; }

        /// <summary>Gets or sets the controller value the angle at <see cref="InputMin"/> produces.</summary>
        public float OutputMin { get; set; }

        /// <summary>Gets or sets the controller value the angle at <see cref="InputMax"/> produces.</summary>
        public float OutputMax { get; set; }
    }

    /// <summary>
    /// Poses output bones from the distances of input bones to authored poses, with the weights solved offline.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CBoneConstraintRbf">CBoneConstraintRbf</seealso>
    public class RbfConstraint : BoneConstraintBase
    {
        /// <summary>Initializes a new instance from compiled constraint data.</summary>
        public RbfConstraint(KVObject data)
        {
            InputBoneHashes = [.. ReadObjects(data, "m_inputBones").Select(ReadBoneReference)];
            OutputBoneHashes = [.. ReadObjects(data, "m_outputBones").Select(ReadBoneReference)];
            Parameters = data.GetArray<byte>("m_rbfParameters") ?? [];
        }

        private static uint ReadBoneReference(KVObject bone) => bone.ValueType == KVValueType.String
            ? StringToken.Get((string)bone)
            : ReadBoneHash(bone);

        /// <summary>Gets or sets the bones whose local transforms are measured.</summary>
        public uint[] InputBoneHashes { get; set; }

        /// <summary>Gets or sets the bones whose local transforms are written.</summary>
        public uint[] OutputBoneHashes { get; set; }

        /// <summary>Gets or sets the solved interpolation data blob.</summary>
        public byte[] Parameters { get; set; }
    }
}
