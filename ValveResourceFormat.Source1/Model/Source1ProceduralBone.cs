using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// A bone whose model-space transform is computed from other bones instead of the animated pose.
/// Rules are evaluated in bone order after the animated pose is placed in model space.
/// </summary>
internal abstract class Source1ProceduralBone
{
    /// <summary>Bone flag: the bone is driven by its procedural rule.</summary>
    public const int AlwaysProceduralBoneFlag = 0x4;

    private const int AxisInterpType = 1;
    private const int QuatInterpType = 2;
    private const int AimAtBoneType = 3;
    private const int AimAtAttachmentType = 4;
    private const int JiggleType = 5;

    private const FeJiggleBoneFlags SimulatedJiggleFlags = FeJiggleBoneFlags.Flexible | FeJiggleBoneFlags.Rigid
        | FeJiggleBoneFlags.YawConstraint | FeJiggleBoneFlags.PitchConstraint | FeJiggleBoneFlags.AngleConstraint
        | FeJiggleBoneFlags.LengthConstraint | FeJiggleBoneFlags.BaseSpring;

    /// <summary>Dot product at which two directions count as parallel.</summary>
    protected const float DegenerateDot = 0.9999999f;
    private const float MinimumLengthSquared = 1e-12f;

    /// <summary>
    /// Replaces the model-space transform of a bone. On entry, <paramref name="boneToModel"/> holds the final
    /// transforms of the bones before <paramref name="boneIndex"/> and the animated transform of the bone itself.
    /// </summary>
    /// <param name="model">Model the bone belongs to.</param>
    /// <param name="boneIndex">Index of the procedural bone.</param>
    /// <param name="boneToModel">Row-vector model-space transforms of the bones.</param>
    public abstract void Evaluate(Source1Model model, int boneIndex, Span<Matrix4x4> boneToModel);

    /// <summary>Reads the rule of a bone, or null when the bone has none that is evaluated.</summary>
    internal static Source1ProceduralBone? Read(byte[] data, long boneOffset, MdlFile.StudioBone bone)
    {
        if ((bone.Flags & AlwaysProceduralBoneFlag) == 0 || bone.ProcIndex == 0)
        {
            return null;
        }

        var offset = boneOffset + bone.ProcIndex;

        return bone.ProcType switch
        {
            AxisInterpType => ReadAxisInterp(data, offset),
            QuatInterpType => ReadQuatInterp(data, offset),
            AimAtBoneType => ReadAimAt(data, offset, false),
            AimAtAttachmentType => ReadAimAt(data, offset, true),
            _ => null,
        };
    }

    /// <summary>Reads the jiggle parameters of a bone, or null when the bone is not a jiggle bone.</summary>
    internal static FeModel.FeJiggleBone? ReadJiggle(byte[] data, long boneOffset, MdlFile.StudioBone bone)
    {
        if ((bone.Flags & AlwaysProceduralBoneFlag) == 0 || bone.ProcIndex == 0 || bone.ProcType != JiggleType)
        {
            return null;
        }

        var jiggle = MemoryMarshal.Read<MdlFile.StudioJiggleBone>(Slice(data, boneOffset + bone.ProcIndex, Marshal.SizeOf<MdlFile.StudioJiggleBone>()));

        // The boing bit of a Source 1 jiggle bone is not a Source 2 flag
        var flags = (FeJiggleBoneFlags)jiggle.Flags & SimulatedJiggleFlags;

        return new FeModel.FeJiggleBone(
            (uint)flags, jiggle.Length, jiggle.TipMass,
            jiggle.YawStiffness, jiggle.YawDamping, jiggle.PitchStiffness, jiggle.PitchDamping,
            jiggle.AlongStiffness, jiggle.AlongDamping, jiggle.AngleLimit,
            jiggle.MinYaw, jiggle.MaxYaw, jiggle.YawFriction, jiggle.YawBounce,
            jiggle.MinPitch, jiggle.MaxPitch, jiggle.PitchFriction, jiggle.PitchBounce,
            jiggle.BaseMass, jiggle.BaseStiffness, jiggle.BaseDamping,
            jiggle.BaseMinLeft, jiggle.BaseMaxLeft, jiggle.BaseLeftFriction,
            jiggle.BaseMinUp, jiggle.BaseMaxUp, jiggle.BaseUpFriction,
            jiggle.BaseMinForward, jiggle.BaseMaxForward, jiggle.BaseForwardFriction,
            0f, 0f, Vector3.Zero, Vector3.Zero, 0);
    }

    private static Source1AxisInterpBone ReadAxisInterp(byte[] data, long offset)
    {
        var span = Slice(data, offset, 176);
        var positions = new Vector3[6];
        var rotations = new Quaternion[6];

        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = MemoryMarshal.Read<Vector3>(span[(8 + (i * 12))..]);
            rotations[i] = MemoryMarshal.Read<Quaternion>(span[(80 + (i * 16))..]);
        }

        return new Source1AxisInterpBone
        {
            Control = MemoryMarshal.Read<int>(span),
            Axis = MemoryMarshal.Read<int>(span[4..]),
            Positions = positions,
            Rotations = rotations,
        };
    }

    private static Source1QuatInterpBone ReadQuatInterp(byte[] data, long offset)
    {
        var span = Slice(data, offset, 12);
        var count = MemoryMarshal.Read<int>(span[4..]);
        var triggerOffset = offset + MemoryMarshal.Read<int>(span[8..]);
        var triggers = new Source1QuatInterpTrigger[Math.Max(count, 0)];

        for (var i = 0; i < triggers.Length; i++)
        {
            var trigger = Slice(data, triggerOffset + (i * 48L), 48);

            triggers[i] = new Source1QuatInterpTrigger
            {
                InverseToleranceAngle = MemoryMarshal.Read<float>(trigger),
                Trigger = MemoryMarshal.Read<Quaternion>(trigger[4..]),
                Position = MemoryMarshal.Read<Vector3>(trigger[20..]),
                Rotation = MemoryMarshal.Read<Quaternion>(trigger[32..]),
            };
        }

        return new Source1QuatInterpBone
        {
            Control = MemoryMarshal.Read<int>(span),
            Triggers = triggers,
        };
    }

    private static Source1AimAtBone ReadAimAt(byte[] data, long offset, bool aimsAtAttachment)
    {
        var span = Slice(data, offset, 44);

        return new Source1AimAtBone
        {
            Parent = MemoryMarshal.Read<int>(span),
            Aim = MemoryMarshal.Read<int>(span[4..]),
            AimVector = MemoryMarshal.Read<Vector3>(span[8..]),
            UpVector = MemoryMarshal.Read<Vector3>(span[20..]),
            BasePosition = MemoryMarshal.Read<Vector3>(span[32..]),
            AimsAtAttachment = aimsAtAttachment,
        };
    }

    private static ReadOnlySpan<byte> Slice(byte[] data, long offset, int length)
    {
        if (offset < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Procedural bone data is out of range.");
        }

        return data.AsSpan((int)offset, length);
    }

    /// <summary>Places a bone from a rotation and translation relative to its parent.</summary>
    protected static void SetFromParent(Source1Model model, int boneIndex, Quaternion rotation, Vector3 position, Span<Matrix4x4> boneToModel)
    {
        var local = Matrix4x4.CreateFromQuaternion(rotation);
        local.Translation = position;

        var parent = model.Bones[boneIndex].ParentIndex;
        boneToModel[boneIndex] = (uint)parent < (uint)boneIndex ? local * boneToModel[parent] : local;
    }

    /// <summary>Expresses a model-space direction in the frame of a model-space transform.</summary>
    protected static Vector3 InverseRotate(Vector3 value, Matrix4x4 frame)
    {
        var xAxis = new Vector3(frame.M11, frame.M12, frame.M13);
        var yAxis = new Vector3(frame.M21, frame.M22, frame.M23);
        var zAxis = new Vector3(frame.M31, frame.M32, frame.M33);

        return new Vector3(Vector3.Dot(value, xAxis), Vector3.Dot(value, yAxis), Vector3.Dot(value, zAxis));
    }

    /// <summary>Whether a bone index refers to a bone that is already placed.</summary>
    protected static bool IsEarlierBone(int index, int boneIndex) => (uint)index < (uint)boneIndex;

    /// <summary>Normalizes a vector, leaving a vector too short to have a direction as zero.</summary>
    protected static Vector3 NormalizeOrZero(Vector3 value) => MathUtils.SafeNormalize(value, Vector3.Zero, MinimumLengthSquared);
}

/// <summary>
/// Blends six poses by the direction of an axis of a control bone, relative to the parent of the control bone.
/// The poses are the directions +X, -X, +Y, -Y, +Z and -Z.
/// </summary>
internal sealed class Source1AxisInterpBone : Source1ProceduralBone
{
    /// <summary>Bone whose axis drives the blend.</summary>
    public required int Control { get; init; }

    /// <summary>Axis of the control bone, 0 for X, 1 for Y and 2 for Z.</summary>
    public required int Axis { get; init; }

    /// <summary>Translation of each of the six poses, relative to the parent.</summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>Rotation of each of the six poses, relative to the parent.</summary>
    public required Quaternion[] Rotations { get; init; }

    /// <inheritdoc/>
    public override void Evaluate(Source1Model model, int boneIndex, Span<Matrix4x4> boneToModel)
    {
        if (!IsEarlierBone(Control, boneIndex) || (uint)Axis > 2u)
        {
            return;
        }

        var controlWorld = boneToModel[Control];
        var direction = Axis switch
        {
            0 => new Vector3(controlWorld.M11, controlWorld.M12, controlWorld.M13),
            1 => new Vector3(controlWorld.M21, controlWorld.M22, controlWorld.M23),
            _ => new Vector3(controlWorld.M31, controlWorld.M32, controlWorld.M33),
        };

        var controlParent = model.Bones[Control].ParentIndex;

        if (controlParent >= 0)
        {
            direction = InverseRotate(direction, boneToModel[controlParent]);
        }

        var xSlot = direction.X < 0f ? 1 : 0;
        var ySlot = direction.Y < 0f ? 3 : 2;
        var zSlot = direction.Z < 0f ? 5 : 4;
        var xWeight = MathF.Abs(direction.X);
        var yWeight = MathF.Abs(direction.Y);
        var zWeight = MathF.Abs(direction.Z);

        Quaternion rotation;
        Vector3 position;

        if (xWeight + yWeight <= 0f)
        {
            rotation = Rotations[zSlot];
            position = Positions[zSlot];
        }
        else
        {
            var scale = 1f / (xWeight + yWeight + zWeight);
            var xyBlend = Source1PoseMath.Slerp(Rotations[ySlot], Rotations[xSlot], xWeight / (xWeight + yWeight));

            rotation = Source1PoseMath.Slerp(xyBlend, Rotations[zSlot], zWeight * scale);
            position = ((Positions[xSlot] * xWeight) + (Positions[ySlot] * yWeight) + (Positions[zSlot] * zWeight)) * scale;
        }

        SetFromParent(model, boneIndex, rotation, position, boneToModel);
    }
}

/// <summary>A pose of a <see cref="Source1QuatInterpBone"/> and the control rotation it applies at.</summary>
internal sealed class Source1QuatInterpTrigger
{
    /// <summary>Reciprocal of the angle at which the trigger stops applying.</summary>
    public required float InverseToleranceAngle { get; init; }

    /// <summary>Control rotation at which the trigger applies fully.</summary>
    public required Quaternion Trigger { get; init; }

    /// <summary>Translation of the pose, relative to the parent.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>Rotation of the pose, relative to the parent.</summary>
    public required Quaternion Rotation { get; init; }
}

/// <summary>
/// Blends the poses of its triggers by how close the rotation of a control bone, relative to the control bone's parent,
/// is to each trigger rotation.
/// </summary>
internal sealed class Source1QuatInterpBone : Source1ProceduralBone
{
    private const float MinimumTotalWeight = 0.001f;

    /// <summary>Bone whose rotation drives the blend.</summary>
    public required int Control { get; init; }

    /// <summary>Triggers of the bone.</summary>
    public required Source1QuatInterpTrigger[] Triggers { get; init; }

    /// <inheritdoc/>
    public override void Evaluate(Source1Model model, int boneIndex, Span<Matrix4x4> boneToModel)
    {
        if (Triggers.Length == 0 || !IsEarlierBone(Control, boneIndex))
        {
            return;
        }

        var controlParent = model.Bones[Control].ParentIndex;

        if (!IsEarlierBone(controlParent, Control) || !Matrix4x4.Invert(boneToModel[controlParent], out var parentInverse))
        {
            return;
        }

        var controlLocal = boneToModel[Control] * parentInverse;
        var controlRotation = Quaternion.CreateFromRotationMatrix(controlLocal);

        Span<float> weights = Triggers.Length <= 32 ? stackalloc float[Triggers.Length] : new float[Triggers.Length];
        var totalWeight = 0f;

        for (var i = 0; i < Triggers.Length; i++)
        {
            var trigger = Triggers[i];
            var dot = MathF.Abs(Quaternion.Dot(trigger.Trigger, controlRotation));
            var weight = MathF.Max(0f, 1f - (trigger.InverseToleranceAngle * 2f * MathUtils.SafeAcos(dot)));

            weights[i] = weight;
            totalWeight += weight;
        }

        Quaternion rotation;
        Vector3 position;

        if (totalWeight <= MinimumTotalWeight)
        {
            rotation = Triggers[0].Rotation;
            position = Triggers[0].Position;
        }
        else
        {
            rotation = default;
            position = Vector3.Zero;

            for (var i = 0; i < Triggers.Length; i++)
            {
                if (weights[i] == 0f)
                {
                    continue;
                }

                var share = weights[i] / totalWeight;
                var aligned = Source1PoseMath.Align(rotation, Triggers[i].Rotation);

                rotation += aligned * share;
                position += Triggers[i].Position * share;
            }
        }

        SetFromParent(model, boneIndex, rotation, position, boneToModel);
    }
}

/// <summary>A bone that points a vector of its own at a bone or an attachment, replacing its model-space transform.</summary>
internal sealed class Source1AimAtBone : Source1ProceduralBone
{
    /// <summary>Bone the position of the bone is relative to, -1 for the model origin.</summary>
    public required int Parent { get; init; }

    /// <summary>Bone or attachment index to aim at.</summary>
    public required int Aim { get; init; }

    /// <summary>Whether <see cref="Aim"/> is an attachment instead of a bone.</summary>
    public required bool AimsAtAttachment { get; init; }

    /// <summary>Direction that points at the target, in the frame of the bone.</summary>
    public required Vector3 AimVector { get; init; }

    /// <summary>Direction that is kept as close to the natural up direction as the aim allows.</summary>
    public required Vector3 UpVector { get; init; }

    /// <summary>Position relative to <see cref="Parent"/>.</summary>
    public required Vector3 BasePosition { get; init; }

    /// <inheritdoc/>
    public override void Evaluate(Source1Model model, int boneIndex, Span<Matrix4x4> boneToModel)
    {
        if (Parent >= 0 && !IsEarlierBone(Parent, boneIndex))
        {
            return;
        }

        if (!TryGetTarget(model, boneIndex, boneToModel, out var target))
        {
            return;
        }

        var parentWorld = Parent >= 0 ? boneToModel[Parent] : Matrix4x4.Identity;
        var origin = Vector3.Transform(BasePosition, parentWorld);

        var direction = NormalizeOrZero(target - origin);
        var axis = NormalizeOrZero(Vector3.Cross(AimVector, direction));
        var aimRotation = Quaternion.CreateFromAxisAngle(axis, MathUtils.SafeAcos(Vector3.Dot(direction, AimVector)));

        var rotation = aimRotation;

        if (MathF.Abs(Vector3.Dot(UpVector, AimVector)) < DegenerateDot)
        {
            var current = RemoveComponent(Vector3.Transform(UpVector, aimRotation), direction);

            var natural = Matrix4x4.CreateFromQuaternion(model.Bones[boneIndex].Rotation) * parentWorld;
            var wanted = RemoveComponent(Vector3.TransformNormal(UpVector, natural), direction);

            var upDot = Vector3.Dot(wanted, current);
            var upAxis = current;

            if (MathF.Abs(upDot) < DegenerateDot)
            {
                upAxis = NormalizeOrZero(Vector3.Cross(wanted, current));
            }

            var upRotation = Quaternion.CreateFromAxisAngle(upAxis, MathUtils.SafeAcos(upDot));
            rotation = Source1PoseMath.Multiply(upRotation, aimRotation);
        }

        var world = Matrix4x4.CreateFromQuaternion(rotation);
        world.Translation = origin;
        boneToModel[boneIndex] = world;
    }

    private bool TryGetTarget(Source1Model model, int boneIndex, Span<Matrix4x4> boneToModel, out Vector3 target)
    {
        target = default;

        if (!AimsAtAttachment)
        {
            if (!IsEarlierBone(Aim, boneIndex))
            {
                return false;
            }

            target = boneToModel[Aim].Translation;
            return true;
        }

        if ((uint)Aim >= (uint)model.Attachments.Length)
        {
            return false;
        }

        var attachment = model.Attachments[Aim];

        if (!IsEarlierBone(attachment.BoneIndex, boneIndex))
        {
            return false;
        }

        target = Vector3.Transform(attachment.Local.Translation, boneToModel[attachment.BoneIndex]);
        return true;
    }

    private static Vector3 RemoveComponent(Vector3 value, Vector3 direction)
    {
        return NormalizeOrZero(MathUtils.ProjectOntoPlane(value, direction));
    }
}
