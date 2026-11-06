using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// A version 10 studio model. Textures can live in a separate <c>T.mdl</c> file and animations in sequence group files.
/// </summary>
internal sealed class GoldSrcMdlFile
{
    public const int SupportedVersion = 10;
    private const int Identifier = 0x54534449;
    private const int SequenceGroupIdentifier = 0x51534449;
    private const int HeaderSize = 244;
    private const int SequenceGroupHeaderSize = 76;
    private const int BoneSize = 112;
    private const int BoneControllerSize = 24;
    private const int HitboxSize = 32;
    private const int SequenceGroupSize = 104;
    private const int SequenceSize = 176;
    private const int EventSize = 76;
    private const int AttachmentSize = 88;
    private const int TextureSize = 80;
    private const int BodyPartSize = 76;
    private const int ModelSize = 112;
    private const int MeshSize = 20;
    private const int AnimSize = 12;

    // Bits of a bone controller type that move a channel
    private const int ControllerTranslationMask = 0x7;

    // Bits of a bone controller type that rotate a channel
    private const int ControllerRotationMask = 0x38;

    private const int MaxTextureSize = 4096;

    // Bits of a sequence motion type that name the axes whose movement is applied to the entity
    private const int MotionAxisMask = 0x7;

    // x, y, z, xr, yr, zr
    private const int ControllerChannelCount = 6;

    private readonly byte[] data;
    private readonly Dictionary<int, byte[]> sequenceGroupData = [];

    public string Name { get; }
    public string ModelName { get; }
    public int Flags { get; }
    public Vector3 EyePosition { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 BoundingBoxMin { get; }
    public Vector3 BoundingBoxMax { get; }

    public GoldSrcBone[] Bones { get; }
    public GoldSrcBoneController[] BoneControllers { get; }
    public GoldSrcHitbox[] Hitboxes { get; }
    public GoldSrcSequence[] Sequences { get; }
    public GoldSrcSequenceGroup[] SequenceGroups { get; }
    public GoldSrcMdlTexture[] Textures { get; }
    public GoldSrcAttachment[] Attachments { get; }
    public GoldSrcBodyPart[] BodyParts { get; }

    /// <summary>The skin table: for each skin family, the texture index of every skin reference used by meshes.</summary>
    public short[][] SkinFamilies { get; }

    public int SkinReferenceCount { get; }

    /// <summary>The model that holds the textures when this file has none, loaded from the file named like this one with a T suffix.</summary>
    public GoldSrcMdlFile? TextureFile { get; set; }

    private readonly ModelNameSet boneExportNames = new();

    public GoldSrcMdlFile(byte[] data, string name)
    {
        this.data = data;
        Name = name;

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too small to be a studio model.");
        }

        var identifier = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (identifier != Identifier)
        {
            throw new UnexpectedMagicException("Not a studio model", identifier, nameof(identifier));
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));

        if (version != SupportedVersion)
        {
            throw new UnexpectedMagicException($"Unsupported studio model version, supported version is {SupportedVersion}", version, nameof(version));
        }

        ModelName = PalettedImage.ReadName(data.AsSpan(8, 64));
        EyePosition = ReadVector3(76);
        Min = ReadVector3(88);
        Max = ReadVector3(100);
        BoundingBoxMin = ReadVector3(112);
        BoundingBoxMax = ReadVector3(124);
        Flags = ReadInt(136);

        Bones = ReadArray(ReadInt(140), ReadInt(144), BoneSize, ReadBone);
        BoneControllers = ReadArray(ReadInt(148), ReadInt(152), BoneControllerSize, ReadBoneController);
        Hitboxes = ReadArray(ReadInt(156), ReadInt(160), HitboxSize, ReadHitbox);
        Sequences = ReadArray(ReadInt(164), ReadInt(168), SequenceSize, ReadSequence);
        SequenceGroups = ReadArray(ReadInt(172), ReadInt(176), SequenceGroupSize, ReadSequenceGroup);
        Textures = ReadArray(ReadInt(180), ReadInt(184), TextureSize, ReadTexture);

        SkinReferenceCount = ReadInt(192);
        var skinFamilyCount = ReadInt(196);
        var skinIndex = ReadInt(200);
        SkinFamilies = ReadSkins(SkinReferenceCount, skinFamilyCount, skinIndex);

        BodyParts = ReadArray(ReadInt(204), ReadInt(208), BodyPartSize, ReadBodyPart);
        Attachments = ReadArray(ReadInt(212), ReadInt(216), AttachmentSize, ReadAttachment);
    }

    /// <summary>
    /// Whether the file starts like a supported studio model.
    /// </summary>
    public static bool IsModel(ReadOnlySpan<byte> header) => HasSupportedVersion(header, Identifier);

    private static bool HasIdentifier(ReadOnlySpan<byte> header, int identifier)
        => header.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(header) == identifier;

    private static bool HasSupportedVersion(ReadOnlySpan<byte> header, int identifier)
        => header.Length >= 8 && HasIdentifier(header, identifier) && BinaryPrimitives.ReadInt32LittleEndian(header[4..]) == SupportedVersion;

    /// <summary>
    /// Reads a model together with the files it keeps its textures and animations in.
    /// </summary>
    /// <param name="data">The contents of the model.</param>
    /// <param name="name">The name of the model without its extension, which its companion files are named after.</param>
    /// <param name="readCompanion">Reads a file that sits next to the model by its file name, and returns null when there is none.</param>
    public static GoldSrcMdlFile Load(byte[] data, string name, Func<string, byte[]?> readCompanion)
    {
        var mdl = new GoldSrcMdlFile(data, name);

        if (!mdl.HasTextures)
        {
            var textureData = readCompanion(name + "T.mdl");

            if (textureData != null)
            {
                mdl.TextureFile = new GoldSrcMdlFile(textureData, name + "T");
            }
        }

        for (var group = 1; group < mdl.SequenceGroups.Length; group++)
        {
            var groupData = readCompanion(mdl.GetSequenceGroupFileName(group));

            if (groupData != null)
            {
                mdl.SetSequenceGroupData(group, groupData);
            }
        }

        return mdl;
    }

    /// <summary>
    /// Sets the contents of a sequence group file, whose animation data follows its 76 byte header.
    /// </summary>
    public void SetSequenceGroupData(int group, byte[] groupData)
    {
        if (groupData.Length < SequenceGroupHeaderSize)
        {
            throw new InvalidDataException("File is too small to be a sequence group file.");
        }

        var identifier = BinaryPrimitives.ReadInt32LittleEndian(groupData);

        if (identifier != SequenceGroupIdentifier)
        {
            throw new UnexpectedMagicException("Not a sequence group file", identifier, nameof(identifier));
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(groupData.AsSpan(4));

        if (version != SupportedVersion)
        {
            throw new UnexpectedMagicException($"Unsupported sequence group file version, supported version is {SupportedVersion}", version, nameof(version));
        }

        sequenceGroupData[group] = groupData;
    }

    /// <summary>
    /// Whether the animations of a sequence are available: they are in this file or in a group file that was set.
    /// </summary>
    public bool HasAnimationData(GoldSrcSequence sequence)
        => sequence.SequenceGroup == 0 || sequenceGroupData.ContainsKey(sequence.SequenceGroup);

    /// <summary>
    /// Whether the file has textures or is the texture file of another model.
    /// </summary>
    public bool HasTextures => Textures.Length > 0;

    /// <summary>
    /// Reads a texture as an image with the palette that follows its pixels.
    /// </summary>
    public PalettedImage ReadTextureImage(GoldSrcMdlTexture texture)
    {
        if (texture.Width <= 0 || texture.Height <= 0 || texture.Width > MaxTextureSize || texture.Height > MaxTextureSize)
        {
            throw new InvalidDataException($"Texture \"{texture.Name}\" has invalid dimensions {texture.Width}x{texture.Height}.");
        }

        var pixelCount = texture.Width * texture.Height;

        if (texture.DataOffset < 0 || texture.DataOffset > data.Length - pixelCount - PalettedImage.PaletteSize)
        {
            throw new InvalidDataException($"Texture \"{texture.Name}\" is out of range.");
        }

        var pixels = data.AsSpan(texture.DataOffset, pixelCount).ToArray();
        var palette = data.AsSpan(texture.DataOffset + pixelCount, PalettedImage.PaletteSize).ToArray();

        return new PalettedImage
        {
            Name = texture.Name,
            Width = texture.Width,
            Height = texture.Height,
            Mips = [pixels],
            Palette = palette,
        };
    }

    /// <summary>
    /// Gets the name of the file that holds the animations of a sequence group other than the first.
    /// </summary>
    public string GetSequenceGroupFileName(int group) => string.Create(CultureInfo.InvariantCulture, $"{Name}{group:00}.mdl");

    /// <summary>
    /// Gets the local pose of every bone at a frame of one animation of a sequence.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="animation">The index of the animation within the blends of the sequence.</param>
    /// <param name="frame">The frame of the animation.</param>
    /// <param name="controllerValues">The value of each bone controller in its own units, or empty to leave them at their defaults.</param>
    /// <param name="positions">Receives the position of every bone.</param>
    /// <param name="rotations">Receives the rotation of every bone.</param>
    public void GetAnimationPose(GoldSrcSequence sequence, int animation, float frame, ReadOnlySpan<float> controllerValues, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        Span<float> adjust = stackalloc float[BoneControllers.Length];
        GetControllerAdjustments(controllerValues, adjust);

        EvaluateAnimation(sequence, animation, frame, adjust, positions, rotations);
    }

    /// <summary>
    /// Gets the value a bone controller has when nothing sets it: zero, held within the range of the controller.
    /// </summary>
    public float GetDefaultControllerValue(int controller)
    {
        var boneController = BoneControllers[controller];
        var low = MathF.Min(boneController.Start, boneController.End);
        var high = MathF.Max(boneController.Start, boneController.End);
        return Math.Clamp(0f, low, high);
    }

    // Rotating controllers are in degrees, and the channels they move are in radians
    private void GetControllerAdjustments(ReadOnlySpan<float> values, Span<float> adjust)
    {
        for (var i = 0; i < adjust.Length; i++)
        {
            var value = i < values.Length ? values[i] : GetDefaultControllerValue(i);
            var type = BoneControllers[i].Type;

            if ((type & ControllerRotationMask) != 0)
            {
                adjust[i] = float.DegreesToRadians(value);
            }
            else if ((type & ControllerTranslationMask) != 0)
            {
                adjust[i] = value;
            }
            else
            {
                adjust[i] = 0f;
            }
        }
    }

    private void EvaluateAnimation(GoldSrcSequence sequence, int blend, float frame, ReadOnlySpan<float> adjust, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var source = GetAnimationData(sequence.SequenceGroup);
        var lastFrame = Math.Max(1, sequence.FrameCount) - 1;
        var clampedFrame = Math.Clamp(frame, 0f, lastFrame);
        var firstFrame = (int)clampedFrame;
        var secondFrame = Math.Min(firstFrame + 1, lastFrame);
        var fraction = clampedFrame - firstFrame;
        var animBase = sequence.AnimIndex + (blend * Bones.Length * AnimSize);

        Span<float> first = stackalloc float[ControllerChannelCount];
        Span<float> second = stackalloc float[ControllerChannelCount];

        for (var i = 0; i < Bones.Length; i++)
        {
            var bone = Bones[i];
            var offsetsStart = animBase + (i * AnimSize);
            CheckAnimationRange(source, offsetsStart, AnimSize);

            ReadChannels(bone, source, offsetsStart, firstFrame, adjust, first);
            positions[i] = new Vector3(first[0], first[1], first[2]);
            rotations[i] = EulerToQuaternion(first[3], first[4], first[5]);

            if (fraction > 0f && secondFrame != firstFrame)
            {
                ReadChannels(bone, source, offsetsStart, secondFrame, adjust, second);

                var secondPosition = new Vector3(second[0], second[1], second[2]);
                var secondRotation = EulerToQuaternion(second[3], second[4], second[5]);
                positions[i] = Vector3.Lerp(positions[i], secondPosition, fraction);
                rotations[i] = Quaternion.Slerp(rotations[i], secondRotation, fraction);
            }
        }

        // The movement of a sequence is applied to the entity, so the bone that carries it stays in place
        if ((uint)sequence.MotionBone < (uint)Bones.Length)
        {
            var position = positions[sequence.MotionBone];
            var motion = sequence.MotionType & MotionAxisMask;

            positions[sequence.MotionBone] = new Vector3(
                (motion & 1) != 0 ? 0f : position.X,
                (motion & 2) != 0 ? 0f : position.Y,
                (motion & 4) != 0 ? 0f : position.Z);
        }
    }

    private static void ReadChannels(GoldSrcBone bone, byte[] source, int offsetsStart, int frame, ReadOnlySpan<float> adjust, Span<float> channels)
    {
        for (var j = 0; j < ControllerChannelCount; j++)
        {
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offsetsStart + (j * 2)));
            channels[j] = bone.Value[j];

            if (offset != 0)
            {
                channels[j] += DecodeAnimationValue(source, offsetsStart + offset, frame) * bone.Scale[j];
            }

            var controller = bone.Controllers[j];

            if ((uint)controller < (uint)adjust.Length)
            {
                channels[j] += adjust[controller];
            }
        }
    }

    /// <summary>
    /// Builds the rotation from angles in radians about x (roll), y (pitch) and z (yaw), applied as z, then y, then x.
    /// </summary>
    public static Quaternion EulerToQuaternion(float roll, float pitch, float yaw)
        => EntityTransformHelper.EulerAnglesToQuaternion(Vector3.RadiansToDegrees(new Vector3(pitch, yaw, roll)));

    // Each run stores "valid" values out of "total" frames, and the last stored value repeats for the rest of the run.
    private static float DecodeAnimationValue(byte[] source, int start, int frame)
    {
        var position = start;
        var remaining = frame;

        CheckAnimationRange(source, position, 2);

        while (source[position + 1] <= remaining)
        {
            var total = source[position + 1];

            if (total == 0)
            {
                return 0f;
            }

            remaining -= total;
            position += (source[position] + 1) * 2;
            CheckAnimationRange(source, position, 2);
        }

        var valid = source[position];
        var index = valid > remaining ? remaining + 1 : valid;
        var valuePosition = position + (index * 2);
        CheckAnimationRange(source, valuePosition, 2);

        return BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(valuePosition));
    }

    private static void CheckAnimationRange(byte[] source, int position, int length)
    {
        if (position < 0 || position > source.Length - length)
        {
            throw new InvalidDataException("Animation data is out of range.");
        }
    }

    private byte[] GetAnimationData(int group)
    {
        if (group == 0)
        {
            return data;
        }

        if (!sequenceGroupData.TryGetValue(group, out var groupData))
        {
            throw new InvalidDataException($"Sequence group {group} file ({GetSequenceGroupFileName(group)}) is not loaded.");
        }

        return groupData;
    }

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
    private float ReadFloat(int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
    private short ReadShort(int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset));
    private Vector3 ReadVector3(int offset) => new(ReadFloat(offset), ReadFloat(offset + 4), ReadFloat(offset + 8));
    private string ReadString(int offset, int length) => PalettedImage.ReadName(data.AsSpan(offset, length));

    private float[] ReadFloats(int offset, int count)
    {
        var result = new float[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = ReadFloat(offset + (i * 4));
        }

        return result;
    }

    private int[] ReadInts(int offset, int count)
    {
        var result = new int[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = ReadInt(offset + (i * 4));
        }

        return result;
    }

    private void CheckTable(string description, int offset, int count, long stride)
    {
        if (count < 0 || stride < 0 || offset < 0 || (long)offset + (count * stride) > data.Length)
        {
            throw new InvalidDataException($"{description} is out of range.");
        }
    }

    private T[] ReadArray<T>(int count, int offset, int stride, Func<int, T> read)
    {
        if (count == 0)
        {
            return [];
        }

        CheckTable($"{typeof(T).Name} table", offset, count, stride);

        var result = new T[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = read(offset + (i * stride));
        }

        return result;
    }

    private GoldSrcBone ReadBone(int at)
    {
        var name = ReadString(at, 32);

        return new GoldSrcBone
        {
            Name = name,
            ExportName = boneExportNames.Create(name),
            Parent = ReadInt(at + 32),
            Flags = ReadInt(at + 36),
            Controllers = ReadInts(at + 40, ControllerChannelCount),
            Value = ReadFloats(at + 64, ControllerChannelCount),
            Scale = ReadFloats(at + 88, ControllerChannelCount),
        };
    }

    private GoldSrcBoneController ReadBoneController(int at) => new()
    {
        Bone = ReadInt(at),
        Type = ReadInt(at + 4),
        Start = ReadFloat(at + 8),
        End = ReadFloat(at + 12),
        Index = ReadInt(at + 20),
    };

    private GoldSrcHitbox ReadHitbox(int at) => new()
    {
        Bone = ReadInt(at),
        Group = ReadInt(at + 4),
        Min = ReadVector3(at + 8),
        Max = ReadVector3(at + 20),
    };

    private GoldSrcSequenceGroup ReadSequenceGroup(int at) => new()
    {
        Label = ReadString(at, 32),
        Name = ReadString(at + 32, 64),
    };

    private GoldSrcAttachment ReadAttachment(int at) => new()
    {
        Name = ReadString(at, 32),
        Type = ReadInt(at + 32),
        Bone = ReadInt(at + 36),
        Origin = ReadVector3(at + 40),
    };

    private GoldSrcMdlTexture ReadTexture(int at) => new()
    {
        Name = ReadString(at, 64),
        Flags = (GoldSrcTextureFlags)ReadInt(at + 64),
        Width = ReadInt(at + 68),
        Height = ReadInt(at + 72),
        DataOffset = ReadInt(at + 76),
    };

    private GoldSrcSequence ReadSequence(int at)
    {
        var eventCount = ReadInt(at + 48);
        var eventIndex = ReadInt(at + 52);

        return new GoldSrcSequence
        {
            Label = ReadString(at, 32),
            Fps = ReadFloat(at + 32),
            Flags = ReadInt(at + 36),
            Activity = ReadInt(at + 40),
            ActivityWeight = ReadInt(at + 44),
            FrameCount = ReadInt(at + 56),
            MotionType = ReadInt(at + 68),
            MotionBone = ReadInt(at + 72),
            LinearMovement = ReadVector3(at + 76),
            Min = ReadVector3(at + 96),
            Max = ReadVector3(at + 108),
            BlendCount = ReadInt(at + 120),
            BlendType = ReadInts(at + 128, 2),
            BlendStart = ReadFloats(at + 136, 2),
            BlendEnd = ReadFloats(at + 144, 2),
            AnimIndex = ReadInt(at + 124),
            SequenceGroup = ReadInt(at + 156),
            EntryNode = ReadInt(at + 160),
            ExitNode = ReadInt(at + 164),
            NodeFlags = ReadInt(at + 168),
            NextSequence = ReadInt(at + 172),
            Events = ReadArray(eventCount, eventIndex, EventSize, ReadEvent),
        };
    }

    private GoldSrcSequenceEvent ReadEvent(int at) => new()
    {
        Frame = ReadInt(at),
        Event = ReadInt(at + 4),
        Options = ReadString(at + 12, 64),
    };

    private short[][] ReadSkins(int referenceCount, int familyCount, int offset)
    {
        if (familyCount == 0 || referenceCount == 0)
        {
            return [];
        }

        CheckTable("Skin table", offset, referenceCount, 2L * familyCount);

        var families = new short[familyCount][];

        for (var i = 0; i < familyCount; i++)
        {
            families[i] = new short[referenceCount];

            for (var j = 0; j < referenceCount; j++)
            {
                families[i][j] = ReadShort(offset + (((i * referenceCount) + j) * 2));
            }
        }

        return families;
    }

    private GoldSrcBodyPart ReadBodyPart(int at) => new()
    {
        Name = ReadString(at, 64),
        Base = ReadInt(at + 68),
        Models = ReadArray(ReadInt(at + 64), ReadInt(at + 72), ModelSize, ReadSubmodel),
    };

    private GoldSrcMdlSubmodel ReadSubmodel(int at)
    {
        var meshCount = ReadInt(at + 72);
        var meshIndex = ReadInt(at + 76);
        var vertexCount = ReadInt(at + 80);
        var vertexInfoIndex = ReadInt(at + 84);
        var vertexIndex = ReadInt(at + 88);
        var normalCount = ReadInt(at + 92);
        var normalInfoIndex = ReadInt(at + 96);
        var normalIndex = ReadInt(at + 100);

        return new GoldSrcMdlSubmodel
        {
            Name = ReadString(at, 64),
            Positions = ReadVectors(vertexCount, vertexIndex),
            PositionBones = ReadBoneIndices(vertexCount, vertexInfoIndex),
            Normals = ReadVectors(normalCount, normalIndex),
            NormalBones = ReadBoneIndices(normalCount, normalInfoIndex),
            Meshes = ReadArray(meshCount, meshIndex, MeshSize, at => ReadMesh(at, vertexCount, normalCount)),
        };
    }

    private Vector3[] ReadVectors(int count, int offset) => ReadArray(count, offset, 12, ReadVector3);

    private byte[] ReadBoneIndices(int count, int offset)
    {
        if (count == 0)
        {
            return [];
        }

        CheckTable("Vertex bone table", offset, count, 1);

        var bones = data.AsSpan(offset, count).ToArray();

        foreach (var bone in bones)
        {
            if (bone >= Bones.Length)
            {
                throw new InvalidDataException("A vertex is on a bone that the model does not have.");
            }
        }

        return bones;
    }

    // A mesh is a series of triangle strips (positive count) and fans (negative count), ended by a zero count.
    private GoldSrcMdlMesh ReadMesh(int at, int positionCount, int normalCount)
    {
        var position = ReadInt(at + 4);
        var vertices = new List<GoldSrcMdlVertex>();
        var indices = new List<int>();

        while (position >= 0 && position + 2 <= data.Length)
        {
            var count = ReadShort(position);
            position += 2;

            if (count == 0)
            {
                break;
            }

            var isFan = count < 0;
            var vertexCount = Math.Abs(count);

            if (position + (vertexCount * 8) > data.Length)
            {
                throw new InvalidDataException("Triangle commands are out of range.");
            }

            var first = vertices.Count;

            for (var i = 0; i < vertexCount; i++)
            {
                var vertexAt = position + (i * 8);
                var positionIndex = ReadShort(vertexAt);
                var normalIndex = ReadShort(vertexAt + 2);

                if ((uint)positionIndex >= (uint)positionCount || (uint)normalIndex >= (uint)normalCount)
                {
                    throw new InvalidDataException("Triangle command has a vertex or normal index out of range.");
                }

                vertices.Add(new GoldSrcMdlVertex(positionIndex, normalIndex, ReadShort(vertexAt + 4), ReadShort(vertexAt + 6)));
            }

            position += vertexCount * 8;

            // The commands wind clockwise seen from outside, so the indices are written in reverse to be counter-clockwise
            for (var i = 0; i + 2 < vertexCount; i++)
            {
                if (isFan)
                {
                    indices.Add(first);
                    indices.Add(first + i + 2);
                    indices.Add(first + i + 1);
                }
                else if ((i & 1) == 0)
                {
                    indices.Add(first + i);
                    indices.Add(first + i + 2);
                    indices.Add(first + i + 1);
                }
                else
                {
                    indices.Add(first + i + 1);
                    indices.Add(first + i + 2);
                    indices.Add(first + i);
                }
            }
        }

        return new GoldSrcMdlMesh
        {
            SkinRef = ReadInt(at + 8),
            Vertices = [.. vertices],
            Indices = [.. indices],
        };
    }
}
