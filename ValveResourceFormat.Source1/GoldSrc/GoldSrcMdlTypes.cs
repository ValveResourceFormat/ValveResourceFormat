namespace ValveResourceFormat.GoldSrc;

/// <summary>Flags of a studio texture.</summary>
[Flags]
internal enum GoldSrcTextureFlags
{
    FlatShade = 0x1,
    Chrome = 0x2,
    FullBright = 0x4,
    NoMips = 0x8,
    Alpha = 0x10,
    Additive = 0x20,
    Masked = 0x40,
}

internal sealed class GoldSrcBone
{
    public required string Name { get; init; }
    public required int Parent { get; init; }
    public required int Flags { get; init; }

    /// <summary>The controller index per channel (x, y, z, xr, yr, zr), -1 when unused.</summary>
    public required int[] Controllers { get; init; }

    /// <summary>The rest value per channel (x, y, z, xr, yr, zr).</summary>
    public required float[] Value { get; init; }

    /// <summary>The scale applied to animation values per channel.</summary>
    public required float[] Scale { get; init; }
}

internal sealed class GoldSrcBoneController
{
    public required int Bone { get; init; }
    public required int Type { get; init; }
    public required float Start { get; init; }
    public required float End { get; init; }
    public required int Index { get; init; }
}

internal sealed class GoldSrcHitbox
{
    public required int Bone { get; init; }
    public required int Group { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
}

internal sealed class GoldSrcAttachment
{
    public required string Name { get; init; }
    public required int Type { get; init; }
    public required int Bone { get; init; }
    public required Vector3 Origin { get; init; }
}

internal sealed class GoldSrcSequenceGroup
{
    public required string Label { get; init; }
    public required string Name { get; init; }
}

internal sealed class GoldSrcSequenceEvent
{
    public required int Frame { get; init; }
    public required int Event { get; init; }
    public required string Options { get; init; }
}

internal sealed class GoldSrcSequence
{
    public required string Label { get; init; }
    public required float Fps { get; init; }
    public required int Flags { get; init; }
    public required int Activity { get; init; }
    public required int ActivityWeight { get; init; }
    public required int FrameCount { get; init; }
    public required int MotionType { get; init; }
    public required int MotionBone { get; init; }
    public required Vector3 LinearMovement { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public required int BlendCount { get; init; }

    /// <summary>What the two blend axes are driven by, as bone controller types.</summary>
    public required int[] BlendType { get; init; }

    /// <summary>The value of each blend axis at its first animation.</summary>
    public required float[] BlendStart { get; init; }

    /// <summary>The value of each blend axis at its last animation.</summary>
    public required float[] BlendEnd { get; init; }

    public required int AnimIndex { get; init; }
    public required int SequenceGroup { get; init; }
    public required int EntryNode { get; init; }
    public required int ExitNode { get; init; }
    public required int NodeFlags { get; init; }
    public required int NextSequence { get; init; }
    public required GoldSrcSequenceEvent[] Events { get; init; }

    /// <summary>Whether the sequence loops, the first flag bit.</summary>
    public bool IsLooping => (Flags & 1) != 0;
}

internal sealed class GoldSrcMdlTexture
{
    public required string Name { get; init; }
    public required GoldSrcTextureFlags Flags { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The offset of the pixel data in the file that holds the textures.</summary>
    public required int DataOffset { get; init; }
}

/// <summary>A vertex of a studio mesh, with texture coordinates in texels.</summary>
internal readonly record struct GoldSrcMdlVertex(int Position, int Normal, short S, short T);

internal sealed class GoldSrcMdlMesh
{
    public required int SkinRef { get; init; }

    /// <summary>The vertices of the triangle lists, which index the model's vertex and normal arrays.</summary>
    public required GoldSrcMdlVertex[] Vertices { get; init; }

    /// <summary>Triangle list indices into <see cref="Vertices"/>.</summary>
    public required int[] Indices { get; init; }
}

internal sealed class GoldSrcMdlSubmodel
{
    public required string Name { get; init; }
    public required Vector3[] Positions { get; init; }
    public required byte[] PositionBones { get; init; }
    public required Vector3[] Normals { get; init; }
    public required byte[] NormalBones { get; init; }
    public required GoldSrcMdlMesh[] Meshes { get; init; }
}

internal sealed class GoldSrcBodyPart
{
    public required string Name { get; init; }
    public required int Base { get; init; }
    public required GoldSrcMdlSubmodel[] Models { get; init; }
}
