using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

/// <summary>Studio flags of an animation or a sequence.</summary>
[Flags]
internal enum Source1AnimationFlags
{
    /// <summary>The last frame equals the first frame.</summary>
    Looping = 0x0001,

    /// <summary>Do not interpolate from the previous animation.</summary>
    Snap = 0x0002,

    /// <summary>The animation holds additive data instead of absolute poses.</summary>
    Delta = 0x0004,

    /// <summary>The sequence always plays.</summary>
    AutoPlay = 0x0008,

    /// <summary>Post flag.</summary>
    Post = 0x0010,

    /// <summary>The animation holds no data.</summary>
    AllZeros = 0x0020,

    /// <summary>Data is stored per frame instead of per bone as run-length encoded streams.</summary>
    FrameAnim = 0x0040,

    /// <summary>Cycle is taken from a pose parameter.</summary>
    CyclePose = 0x0080,

    /// <summary>Cycle is taken from a real-time clock.</summary>
    Realtime = 0x0100,

    /// <summary>The sequence has a local context sequence.</summary>
    Local = 0x0200,

    /// <summary>Hidden in selection lists.</summary>
    Hidden = 0x0400,

    /// <summary>Forward declared empty sequence.</summary>
    Override = 0x0800,

    /// <summary>Updated at runtime to an activity index.</summary>
    Activity = 0x1000,

    /// <summary>Updated at runtime to an event index.</summary>
    Event = 0x2000,

    /// <summary>The sequence blends in world space.</summary>
    World = 0x4000,

    /// <summary>Do not force the animation to loop.</summary>
    NoForceLoop = 0x8000,
}

/// <summary>A piece of root motion of an animation. Angles are yaw degrees, distances are Source units.</summary>
/// <param name="EndFrame">Last frame this piece applies to.</param>
/// <param name="Flags">Motion flags.</param>
/// <param name="StartVelocity">Velocity at the start of the piece.</param>
/// <param name="EndVelocity">Velocity at the end of the piece.</param>
/// <param name="Angle">Yaw rotation at the end of the piece.</param>
/// <param name="Vector">Movement relative to the initial angle of the piece.</param>
/// <param name="Position">Position relative to the start of the animation.</param>
internal readonly record struct Source1Movement(int EndFrame, int Flags, float StartVelocity, float EndVelocity, float Angle, Vector3 Vector, Vector3 Position);

/// <summary>An event of a sequence.</summary>
/// <param name="Cycle">Position in the sequence from 0 to 1.</param>
/// <param name="EventId">Numeric event id.</param>
/// <param name="Type">Event type flags.</param>
/// <param name="Options">Option string.</param>
/// <param name="Name">Event name, empty when the model has none.</param>
internal readonly record struct Source1AnimationEvent(float Cycle, int EventId, int Type, string Options, string Name);

/// <summary>A pose parameter that drives the blend axes of sequences.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Start">Minimum value.</param>
/// <param name="End">Maximum value.</param>
/// <param name="Loop">Looping range, 0 when it does not wrap.</param>
internal readonly record struct Source1PoseParameter(string Name, float Start, float End, float Loop);

/// <summary>An animation of a Source 1 model, decoded to bone-local transforms in Source units (Z-up inches).</summary>
internal sealed class Source1Animation
{
    private readonly Source1AnimationSource source;
    private readonly MdlFile.AnimDesc desc;
    private readonly int descOffset;

    internal Source1Animation(Source1AnimationSource source, int index, MdlFile.AnimDesc desc, int descOffset, string name, Source1Movement[] movements, bool isDataAvailable)
    {
        this.source = source;
        this.desc = desc;
        this.descOffset = descOffset;
        Index = index;
        Name = name;
        Movements = movements;
        IsDataAvailable = isDataAvailable;
    }

    /// <summary>Index of the animation in the model file it comes from.</summary>
    public int Index { get; }

    /// <summary>Animation name. Animations of included models usually start with '@'.</summary>
    public string Name { get; }

    /// <summary>Frames per second.</summary>
    public float Fps => desc.Fps;

    /// <summary>Number of frames.</summary>
    public int FrameCount => desc.NumFrames;

    /// <summary>Studio flags.</summary>
    public Source1AnimationFlags Flags => (Source1AnimationFlags)desc.Flags;

    /// <summary>Whether the last frame equals the first frame.</summary>
    public bool IsLooping => (Flags & Source1AnimationFlags.Looping) != 0;

    /// <summary>
    /// Whether the frames hold additive data. Bone positions are offsets and rotations are
    /// offsets to be multiplied onto the pose, and bones without data are zero and identity.
    /// </summary>
    public bool IsDelta => (Flags & Source1AnimationFlags.Delta) != 0;

    /// <summary>Piecewise root motion. It is not part of the bone transforms.</summary>
    public Source1Movement[] Movements { get; }

    /// <summary>False when the animation data lives in an external file that could not be loaded.</summary>
    public bool IsDataAvailable { get; }

    /// <summary>Number of bones of the base model, which is the length <see cref="GetFrame(int, Span{Vector3}, Span{Quaternion})"/> fills.</summary>
    public int BoneCount => source.BaseBoneCount;

    /// <summary>
    /// Decodes a frame to bone-local (parent-space) transforms of every bone of the base model.
    /// Bones without data keep the reference pose of the base model, or zero and identity for additive animations.
    /// </summary>
    /// <param name="frame">Frame index, clamped to the animation.</param>
    /// <param name="positions">Receives local positions, at least <see cref="BoneCount"/> long.</param>
    /// <param name="rotations">Receives local rotations, at least <see cref="BoneCount"/> long.</param>
    public void GetFrame(int frame, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        if (positions.Length < BoneCount || rotations.Length < BoneCount)
        {
            throw new ArgumentException("Output spans are shorter than the bone count.");
        }

        source.Decode(desc, descOffset, frame, positions, rotations);
    }

    /// <summary>
    /// Samples a fractional frame, interpolating rotations with slerp and positions linearly between the two
    /// neighbouring frames. Looping animations wrap around, others clamp to the first and last frame.
    /// </summary>
    /// <param name="frame">Frame position, which may be fractional.</param>
    /// <param name="positions">Receives local positions, at least <see cref="BoneCount"/> long.</param>
    /// <param name="rotations">Receives local rotations, at least <see cref="BoneCount"/> long.</param>
    public void GetFrame(float frame, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        if (positions.Length < BoneCount || rotations.Length < BoneCount)
        {
            throw new ArgumentException("Output spans are shorter than the bone count.");
        }

        var lastFrame = FrameCount - 1;

        if (lastFrame <= 0 || float.IsNaN(frame))
        {
            source.Decode(desc, descOffset, 0, positions, rotations);
            return;
        }

        if (IsLooping)
        {
            frame -= lastFrame * MathF.Floor(frame / lastFrame);
        }
        else
        {
            frame = Math.Clamp(frame, 0f, lastFrame);
        }

        var lowFrame = Math.Min((int)frame, lastFrame);
        var fraction = frame - lowFrame;

        source.Decode(desc, descOffset, lowFrame, positions, rotations);

        if (fraction <= 0f || lowFrame >= lastFrame)
        {
            return;
        }

        var boneCount = BoneCount;
        var nextPositions = boneCount <= 256 ? stackalloc Vector3[boneCount] : new Vector3[boneCount];
        var nextRotations = boneCount <= 256 ? stackalloc Quaternion[boneCount] : new Quaternion[boneCount];

        source.Decode(desc, descOffset, lowFrame + 1, nextPositions, nextRotations);

        for (var i = 0; i < boneCount; i++)
        {
            positions[i] = Vector3.Lerp(positions[i], nextPositions[i], fraction);
            rotations[i] = Source1PoseMath.Slerp(rotations[i], nextRotations[i], fraction);
        }
    }

    /// <summary>
    /// Evaluates the piecewise root motion at a frame, relative to the start of the animation.
    /// The motion is not part of the bone transforms.
    /// </summary>
    /// <param name="frame">Frame position, which may be fractional.</param>
    /// <returns>Translation and yaw in degrees, zero for an animation without movements.</returns>
    public (Vector3 Position, float Yaw) GetMovement(float frame)
    {
        var position = Vector3.Zero;
        var yaw = 0f;
        var previousFrame = 0f;

        foreach (var movement in Movements)
        {
            if (movement.EndFrame >= frame)
            {
                var length = movement.EndFrame - previousFrame;
                var t = length > 0f ? (frame - previousFrame) / length : 0f;
                var distance = (movement.StartVelocity * t) + (0.5f * (movement.EndVelocity - movement.StartVelocity) * t * t);

                position += distance * movement.Vector;
                yaw = (yaw * (1f - t)) + (movement.Angle * t);
                return (position, yaw);
            }

            previousFrame = movement.EndFrame;
            position = movement.Position;
            yaw = movement.Angle;
        }

        return (position, yaw);
    }
}

/// <summary>Flags of an autolayer of a sequence.</summary>
[Flags]
internal enum Source1AutoLayerFlags
{
    /// <summary>Post flag.</summary>
    Post = 0x0010,

    /// <summary>The ramp in and out curve is a spline instead of linear.</summary>
    Spline = 0x0040,

    /// <summary>Biases the ramp to compensate for a weight below 1 when a second layer also accumulates.</summary>
    XFade = 0x0080,

    /// <summary>The layer always blends at 1, ignoring the weight.</summary>
    NoBlend = 0x0200,

    /// <summary>The layer is a local context sequence, applied before the sequence is blended in.</summary>
    Local = 0x1000,

    /// <summary>The layer is driven by a pose parameter instead of the cycle of the parent.</summary>
    Pose = 0x4000,
}

/// <summary>A sequence that plays on top of another sequence over part of its cycle or pose parameter range.</summary>
internal sealed class Source1AutoLayer
{
    /// <summary>Name of the layered sequence.</summary>
    public required string SequenceName { get; init; }

    /// <summary>The layered sequence, null when the model has no sequence of that name.</summary>
    public Source1Sequence? Sequence { get; internal set; }

    /// <summary>Index into the pose parameters of the model that drives the layer, -1 when there is none.</summary>
    public required int PoseParameterIndex { get; init; }

    /// <summary>Layer flags.</summary>
    public required Source1AutoLayerFlags Flags { get; init; }

    /// <summary>Start of the influence.</summary>
    public required float Start { get; init; }

    /// <summary>Start of the full influence.</summary>
    public required float Peak { get; init; }

    /// <summary>End of the full influence.</summary>
    public required float Tail { get; init; }

    /// <summary>End of the influence.</summary>
    public required float End { get; init; }
}

/// <summary>A sequence of a Source 1 model.</summary>
internal sealed partial class Source1Sequence
{
    /// <summary>The model the sequence was loaded for.</summary>
    internal Source1Model? Model { get; init; }

    /// <summary>Autolayers of the sequence.</summary>
    public Source1AutoLayer[] AutoLayers { get; init; } = [];

    /// <summary>Keys of the blend axes when the blend positions are not evenly spaced, empty otherwise.</summary>
    public float[] PoseKeys { get; init; } = [];

    /// <summary>Index into the pose parameters of the model that drives the X blend axis, -1 when none.</summary>
    public int BlendIndexX { get; init; } = -1;

    /// <summary>Index into the pose parameters of the model that drives the Y blend axis, -1 when none.</summary>
    public int BlendIndexY { get; init; } = -1;

    /// <summary>Index into the pose parameters of the model that drives the cycle, -1 when none.</summary>
    public int CyclePoseIndex { get; init; } = -1;

    /// <summary>Whether the sequence holds additive data.</summary>
    public bool IsDelta => (Flags & Source1AnimationFlags.Delta) != 0;

    /// <summary>Index of the sequence in the model file it comes from.</summary>
    public required int Index { get; init; }

    /// <summary>Sequence name.</summary>
    public required string Name { get; init; }

    /// <summary>Activity name, empty when none.</summary>
    public required string ActivityName { get; init; }

    /// <summary>Activity id as compiled into the file.</summary>
    public required int Activity { get; init; }

    /// <summary>Weight for choosing between sequences of one activity.</summary>
    public required int ActivityWeight { get; init; }

    /// <summary>Studio flags.</summary>
    public required Source1AnimationFlags Flags { get; init; }

    /// <summary>Whether the sequence loops.</summary>
    public bool IsLooping => (Flags & Source1AnimationFlags.Looping) != 0;

    /// <summary>Number of blend columns.</summary>
    public required int BlendCountX { get; init; }

    /// <summary>Number of blend rows.</summary>
    public required int BlendCountY { get; init; }

    /// <summary>Animations of the blend grid, row by row (index is y * <see cref="BlendCountX"/> + x).</summary>
    public required Source1Animation[] Blends { get; init; }

    /// <summary>The first animation of the blend grid.</summary>
    public Source1Animation? Animation => Blends.Length > 0 ? Blends[0] : null;

    /// <summary>Frames per second of the first animation.</summary>
    public float Fps => Animation?.Fps ?? 0f;

    /// <summary>Frame count of the first animation.</summary>
    public int FrameCount => Animation?.FrameCount ?? 0;

    /// <summary>Pose parameter of the X blend axis, null when the sequence has no blend.</summary>
    public required Source1PoseParameter? BlendParameterX { get; init; }

    /// <summary>Pose parameter of the Y blend axis, null when unused.</summary>
    public required Source1PoseParameter? BlendParameterY { get; init; }

    /// <summary>Start of the X blend axis, in the units of the pose parameter.</summary>
    public required float BlendStartX { get; init; }

    /// <summary>End of the X blend axis, in the units of the pose parameter.</summary>
    public required float BlendEndX { get; init; }

    /// <summary>Start of the Y blend axis, in the units of the pose parameter.</summary>
    public required float BlendStartY { get; init; }

    /// <summary>End of the Y blend axis, in the units of the pose parameter.</summary>
    public required float BlendEndY { get; init; }

    /// <summary>Weight of each bone of the base model, 0 for bones the sequence does not drive.</summary>
    public required float[] BoneWeights { get; init; }

    /// <summary>Events.</summary>
    public required Source1AnimationEvent[] Events { get; init; }

    /// <summary>Ideal cross fade in time.</summary>
    public required float FadeInTime { get; init; }

    /// <summary>Ideal cross fade out time.</summary>
    public required float FadeOutTime { get; init; }

    /// <summary>Phase used to match the entry gait.</summary>
    public required float EntryPhase { get; init; }

    /// <summary>Phase used to match the exit gait.</summary>
    public required float ExitPhase { get; init; }

    /// <summary>Per sequence bounding box minimum.</summary>
    public required Vector3 BoundingBoxMin { get; init; }

    /// <summary>Per sequence bounding box maximum.</summary>
    public required Vector3 BoundingBoxMax { get; init; }
}

/// <summary>Decodes the animations of one MDL file, remapping its bones to the bones of the base model by name.</summary>
internal sealed class Source1AnimationSource
{
    public required MdlFile Mdl { get; init; }
    public required byte[] MdlData { get; init; }
    public byte[]? AniData { get; init; }
    public required MdlFile.AnimBlock[] Blocks { get; init; }

    public required int[] BoneToBase { get; init; }
    public required int BaseBoneCount { get; init; }
    public required Vector3[] BasePositions { get; init; }
    public required Quaternion[] BaseRotations { get; init; }

    public void Decode(MdlFile.AnimDesc desc, int descOffset, int frame, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var delta = (desc.Flags & (int)Source1AnimationFlags.Delta) != 0;

        for (var i = 0; i < BaseBoneCount; i++)
        {
            positions[i] = delta ? Vector3.Zero : BasePositions[i];
            rotations[i] = delta ? Quaternion.Identity : BaseRotations[i];
        }

        if ((desc.Flags & (int)Source1AnimationFlags.AllZeros) != 0 || desc.NumFrames <= 0)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, desc.NumFrames - 1);

        var block = desc.AnimBlock;
        var index = desc.AnimIndex;
        var localFrame = frame;

        if (desc.SectionFrames != 0)
        {
            int section;

            if (desc.NumFrames > desc.SectionFrames && frame == desc.NumFrames - 1)
            {
                localFrame = 0;
                section = (desc.NumFrames / desc.SectionFrames) + 1;
            }
            else
            {
                section = frame / desc.SectionFrames;
                localFrame = frame - (section * desc.SectionFrames);
            }

            var sectionOffset = (long)descOffset + desc.SectionIndex + (section * 8L);

            if (sectionOffset < 0 || sectionOffset + 8 > MdlData.Length)
            {
                throw new InvalidDataException("Animation section table is out of range.");
            }

            block = BinaryPrimitives.ReadInt32LittleEndian(MdlData.AsSpan((int)sectionOffset));
            index = BinaryPrimitives.ReadInt32LittleEndian(MdlData.AsSpan((int)sectionOffset + 4));
        }

        if (block == -1 || (block == 0 && index == 0))
        {
            return;
        }

        ReadOnlySpan<byte> data;
        int offset;

        if (block == 0)
        {
            data = MdlData;
            offset = descOffset + index;
        }
        else
        {
            if (AniData == null || block >= Blocks.Length || index < 0)
            {
                return;
            }

            var start = Blocks[block].DataStart;
            var end = Blocks[block].DataEnd;

            if (start < 0 || end < start || end > AniData.Length)
            {
                throw new InvalidDataException("Animation block extent is out of range.");
            }

            data = AniData.AsSpan(start, end - start);
            offset = index;
        }

        if ((desc.Flags & (int)Source1AnimationFlags.FrameAnim) != 0)
        {
            DecodeFrameAnim(data, offset, localFrame, positions, rotations);
        }
        else
        {
            DecodeRle(data, offset, localFrame, positions, rotations);
        }
    }

    private void DecodeRle(ReadOnlySpan<byte> data, int offset, int frame, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var boneCount = Mdl.Bones.Length;

        while (true)
        {
            var bone = ReadByte(data, offset);
            var flags = ReadByte(data, offset + 1);
            var next = ReadInt16(data, offset + 2);

            if (bone == 255)
            {
                break;
            }

            if (bone >= boneCount)
            {
                throw new InvalidDataException($"Animation references bone {bone} of {boneCount}.");
            }

            var target = BoneToBase[bone];

            if (target >= 0)
            {
                DecodeRleBone(data, offset, frame, Mdl.Bones[bone], flags, out var position, out var rotation);
                positions[target] = position;
                rotations[target] = rotation;
            }

            if (next == 0)
            {
                break;
            }

            if (next < 4)
            {
                throw new InvalidDataException("Invalid animation bone record size.");
            }

            offset += next;
        }
    }

    private static void DecodeRleBone(ReadOnlySpan<byte> data, int offset, int frame, MdlFile.StudioBone bone, int flags, out Vector3 position, out Quaternion rotation)
    {
        var isDelta = (flags & (int)AnimFlags.Delta) != 0;
        var payload = offset + 4;

        if ((flags & (int)AnimFlags.RawRot) != 0)
        {
            rotation = ReadQuaternion48(data, payload);
        }
        else if ((flags & (int)AnimFlags.RawRot2) != 0)
        {
            rotation = ReadQuaternion64(data, payload);
        }
        else if ((flags & (int)AnimFlags.AnimRot) != 0)
        {
            var scale = bone.RotationScale;
            var angles = new Vector3(
                ExtractAnimValue(data, payload, 0, frame, scale.X),
                ExtractAnimValue(data, payload, 1, frame, scale.Y),
                ExtractAnimValue(data, payload, 2, frame, scale.Z));

            if (!isDelta)
            {
                angles += bone.Rotation;
            }

            rotation = FromRadianEuler(angles);

            if (!isDelta && (bone.Flags & Source1PoseMath.FixedAlignmentBoneFlag) != 0)
            {
                rotation = Source1PoseMath.Align(bone.Alignment, rotation);
            }
        }
        else
        {
            rotation = isDelta ? Quaternion.Identity : bone.Quaternion;
        }

        if ((flags & (int)AnimFlags.RawPos) != 0)
        {
            var rawOffset = payload;

            if ((flags & (int)AnimFlags.RawRot) != 0)
            {
                rawOffset += 6;
            }

            if ((flags & (int)AnimFlags.RawRot2) != 0)
            {
                rawOffset += 8;
            }

            position = ReadVector48(data, rawOffset);
        }
        else if ((flags & (int)AnimFlags.AnimPos) != 0)
        {
            var valuesOffset = payload;

            if ((flags & (int)AnimFlags.AnimRot) != 0)
            {
                valuesOffset += 6;
            }

            var scale = bone.PositionScale;
            position = new Vector3(
                ExtractAnimValue(data, valuesOffset, 0, frame, scale.X),
                ExtractAnimValue(data, valuesOffset, 1, frame, scale.Y),
                ExtractAnimValue(data, valuesOffset, 2, frame, scale.Z));

            if (!isDelta)
            {
                position += bone.Position;
            }
        }
        else
        {
            position = isDelta ? Vector3.Zero : bone.Position;
        }
    }

    private void DecodeFrameAnim(ReadOnlySpan<byte> data, int offset, int frame, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var boneCount = Mdl.Bones.Length;
        var constantsOffset = ReadInt32(data, offset);
        var frameOffset = ReadInt32(data, offset + 4);
        var frameLength = ReadInt32(data, offset + 8);

        var constant = offset + constantsOffset;
        var framePointer = offset + frameOffset + (frame * frameLength);

        for (var i = 0; i < boneCount; i++)
        {
            var flags = ReadByte(data, offset + 24 + i);
            var bone = Mdl.Bones[i];
            var target = BoneToBase[i];

            var rotation = bone.Quaternion;
            var position = bone.Position;

            if ((flags & (int)FrameFlags.AnimRot) != 0)
            {
                rotation = ReadQuaternion48(data, framePointer);
                framePointer += 6;
            }
            else if ((flags & (int)FrameFlags.AnimRot2) != 0)
            {
                rotation = ReadQuaternion48S(data, framePointer);
                framePointer += 6;
            }
            else if ((flags & (int)FrameFlags.ConstRot) != 0)
            {
                rotation = ReadQuaternion48(data, constant);
                constant += 6;
            }
            else if ((flags & (int)FrameFlags.ConstRot2) != 0)
            {
                rotation = ReadQuaternion48S(data, constant);
                constant += 6;
            }

            if ((flags & (int)FrameFlags.AnimPos) != 0)
            {
                position = ReadVector48(data, framePointer);
                framePointer += 6;
            }
            else if ((flags & (int)FrameFlags.ConstPos) != 0)
            {
                position = ReadVector48(data, constant);
                constant += 6;
            }
            else if ((flags & (int)FrameFlags.AnimPos2) != 0)
            {
                position = ReadVector96(data, framePointer);
                framePointer += 12;
            }
            else if ((flags & (int)FrameFlags.ConstPos2) != 0)
            {
                position = ReadVector96(data, constant);
                constant += 12;
            }

            if (target >= 0)
            {
                positions[target] = position;
                rotations[target] = rotation;
            }
        }
    }

    [Flags]
    private enum AnimFlags
    {
        RawPos = 0x01,
        RawRot = 0x02,
        AnimPos = 0x04,
        AnimRot = 0x08,
        Delta = 0x10,
        RawRot2 = 0x20,
    }

    [Flags]
    private enum FrameFlags
    {
        ConstPos = 0x01,
        ConstRot = 0x02,
        AnimPos = 0x04,
        AnimRot = 0x08,
        AnimPos2 = 0x10,
        ConstPos2 = 0x20,
        ConstRot2 = 0x40,
        AnimRot2 = 0x80,
    }

    private static float ExtractAnimValue(ReadOnlySpan<byte> data, int valuePointerOffset, int axis, int frame, float scale)
    {
        var relative = ReadInt16(data, valuePointerOffset + (axis * 2));

        if (relative <= 0)
        {
            return 0f;
        }

        var run = valuePointerOffset + relative;
        var k = frame;

        while (true)
        {
            var valid = ReadByte(data, run);
            var total = ReadByte(data, run + 1);

            if (total == 0)
            {
                throw new InvalidDataException("Animation value stream ended before the requested frame.");
            }

            if (total > k)
            {
                var entry = valid > k ? k + 1 : valid;
                return ReadInt16(data, run + (entry * 2)) * scale;
            }

            k -= total;
            run += (valid + 1) * 2;
        }
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset >= (uint)data.Length)
        {
            throw new InvalidDataException("Animation data read is out of range.");
        }

        return data[offset];
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Animation data read is out of range.");
        }

        return data.Slice(offset, length);
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(Slice(data, offset, 2));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(Slice(data, offset, 2));
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(Slice(data, offset, 4));
    }

    private static Vector3 ReadVector48(ReadOnlySpan<byte> data, int offset)
    {
        return new Vector3(
            (float)BitConverter.Int16BitsToHalf(ReadInt16(data, offset)),
            (float)BitConverter.Int16BitsToHalf(ReadInt16(data, offset + 2)),
            (float)BitConverter.Int16BitsToHalf(ReadInt16(data, offset + 4)));
    }

    private static Vector3 ReadVector96(ReadOnlySpan<byte> data, int offset)
    {
        return MemoryMarshal.Read<Vector3>(Slice(data, offset, 12));
    }

    private static Quaternion ReadQuaternion48(ReadOnlySpan<byte> data, int offset)
    {
        var x = (ReadUInt16(data, offset) - 32768) * (1f / 32768f);
        var y = (ReadUInt16(data, offset + 2) - 32768) * (1f / 32768f);
        var packedZ = ReadUInt16(data, offset + 4);
        var z = ((packedZ & 0x7FFF) - 16384) * (1f / 16384f);
        var w = MathF.Sqrt(MathF.Max(0f, 1f - (x * x) - (y * y) - (z * z)));

        if ((packedZ & 0x8000) != 0)
        {
            w = -w;
        }

        return new Quaternion(x, y, z, w);
    }

    private static Quaternion ReadQuaternion64(ReadOnlySpan<byte> data, int offset)
    {
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(Slice(data, offset, 8));
        var x = ((int)(bits & 0x1FFFFF) - 1048576) * (1f / 1048576.5f);
        var y = ((int)((bits >> 21) & 0x1FFFFF) - 1048576) * (1f / 1048576.5f);
        var z = ((int)((bits >> 42) & 0x1FFFFF) - 1048576) * (1f / 1048576.5f);
        var w = MathF.Sqrt(MathF.Max(0f, 1f - (x * x) - (y * y) - (z * z)));

        if ((bits >> 63) != 0)
        {
            w = -w;
        }

        return new Quaternion(x, y, z, w);
    }

    private static Quaternion ReadQuaternion48S(ReadOnlySpan<byte> data, int offset)
    {
        var packedA = ReadUInt16(data, offset);
        var packedB = ReadUInt16(data, offset + 2);
        var packedC = ReadUInt16(data, offset + 4);

        const float scale = 1f / 23168f;
        var a = ((packedA & 0x7FFF) - 16384) * scale;
        var b = ((packedB & 0x7FFF) - 16384) * scale;
        var c = ((packedC & 0x7FFF) - 16384) * scale;
        var d = MathF.Sqrt(MathF.Max(0f, 1f - (a * a) - (b * b) - (c * c)));

        if ((packedC & 0x8000) != 0)
        {
            d = -d;
        }

        var firstComponent = ((packedA >> 15) * 2) + (packedB >> 15);

        Span<float> components = stackalloc float[4];
        components[firstComponent] = a;
        components[(firstComponent + 1) % 4] = b;
        components[(firstComponent + 2) % 4] = c;
        components[(firstComponent + 3) % 4] = d;

        return new Quaternion(components[0], components[1], components[2], components[3]);
    }

    private static Quaternion FromRadianEuler(Vector3 angles)
    {
        var (sy, cy) = MathF.SinCos(angles.Z * 0.5f);
        var (sp, cp) = MathF.SinCos(angles.Y * 0.5f);
        var (sr, cr) = MathF.SinCos(angles.X * 0.5f);

        var srCp = sr * cp;
        var crSp = cr * sp;
        var crCp = cr * cp;
        var srSp = sr * sp;

        return new Quaternion(
            (srCp * cy) - (crSp * sy),
            (crSp * cy) + (srCp * sy),
            (crCp * sy) - (srSp * cy),
            (crCp * cy) + (srSp * sy));
    }
}
