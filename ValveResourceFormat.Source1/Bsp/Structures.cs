using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspModel
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;
    public readonly Vector3 Origin;
    public readonly int HeadNode;
    public readonly int FirstFace;
    public readonly int NumFaces;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspPlane
{
    public readonly Vector3 Normal;
    public readonly float Dist;
    public readonly int Type;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspBrush
{
    public readonly int FirstSide;
    public readonly int NumSides;
    public readonly BspContents Contents;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspBrushSide
{
    public readonly ushort Plane;
    public readonly short TexInfo;
    public readonly short DispInfo;

    // A short in older maps, whose high byte is then zero
    private readonly byte bevel;
    public readonly byte Thin;

    /// <summary>Gets whether the side is a plane added for collision that the brush was not authored with.</summary>
    public bool IsBevel => bevel != 0;

    public BspBrushSide(in BspBrushSideWide side)
    {
        Plane = BspWide.ToUInt16(side.Plane);
        TexInfo = BspWide.ToInt16(side.TexInfo);
        DispInfo = BspWide.ToInt16(side.DispInfo);
        bevel = side.Bevel;
        Thin = side.Thin;
    }
}

/// <summary>A brush side of maps with 32-bit indices.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
internal readonly struct BspBrushSideWide
{
    public readonly int Plane;
    public readonly int TexInfo;
    public readonly int DispInfo;
    public readonly byte Bevel;
    public readonly byte Thin;
}

/// <summary>What a brush or leaf is made of.</summary>
[Flags]
internal enum BspContents
{
    Empty = 0,
    Solid = 0x1,
    Window = 0x2,
    Aux = 0x4,
    Grate = 0x8,
    Slime = 0x10,
    Water = 0x20,
    BlockLos = 0x40,
    Opaque = 0x80,
    TestFogVolume = 0x100,
    Team1 = 0x800,
    Team2 = 0x1000,
    IgnoreNodrawOpaque = 0x2000,
    Moveable = 0x4000,
    AreaPortal = 0x8000,
    PlayerClip = 0x10000,
    MonsterClip = 0x20000,
    Origin = 0x1000000,
    Monster = 0x2000000,
    Debris = 0x4000000,
    Detail = 0x8000000,
    Translucent = 0x10000000,
    Ladder = 0x20000000,
    Hitbox = 0x40000000,
}

internal static class BspContentsExtensions
{
    /// <summary>
    /// Lists the names of the contents set, and the bits without a name in hexadecimal, since the games give
    /// some bits different meanings.
    /// </summary>
    /// <param name="contents">The contents.</param>
    public static string GetNames(this BspContents contents)
    {
        if (contents == BspContents.Empty)
        {
            return nameof(BspContents.Empty);
        }

        var names = new List<string>();
        var remaining = contents;

        foreach (var flag in Enum.GetValues<BspContents>())
        {
            if (flag != BspContents.Empty && (remaining & flag) == flag)
            {
                names.Add(flag.ToString());
                remaining &= ~flag;
            }
        }

        if (remaining != BspContents.Empty)
        {
            names.Add(string.Create(CultureInfo.InvariantCulture, $"0x{(int)remaining:X}"));
        }

        return string.Join(", ", names);
    }
}

/// <summary>An edge as stored by maps with 32-bit indices.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspEdge
{
    public readonly uint A;
    public readonly uint B;

    public BspEdge(ushort a, ushort b)
    {
        A = a;
        B = b;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspEdge16
{
    public readonly ushort A;
    public readonly ushort B;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspFace
{
    public readonly ushort PlaneNum;
    public readonly byte Side;
    public readonly byte OnNode;
    public readonly int FirstEdge;
    public readonly short NumEdges;
    public readonly short TexInfo;
    public readonly short DispInfo;
    public readonly short FogVolumeId;
    private readonly uint styles;
    public readonly int LightOffset;
    public readonly float Area;
    public readonly int LightMapOffsetX;
    public readonly int LightMapOffsetY;
    public readonly int LightMapSizeX;
    public readonly int LightMapSizeY;
    public readonly int OriginalFace;
    public readonly ushort NumPrimitives;
    public readonly ushort FirstPrimitive;
    public readonly uint SmoothingGroups;

    public bool HasLightmap => LightOffset != -1;

    /// <summary>The number of primitives of the face. The top bit of the stored value disables dynamic shadows.</summary>
    public int PrimitiveCount => NumPrimitives & 0x7fff;

    public byte GetLightStyle(int index) => (byte)((styles >> (index << 3)) & 0xff);

    /// <summary>
    /// Reads a 104-byte face of a map before v19. The plane to light style fields start at byte 32, the light offset
    /// and area follow at 72, then the lightmap bounds, original face and primitives up to the end.
    /// </summary>
    internal static BspFace ReadLegacy(ReadOnlySpan<byte> entry)
    {
        const int Size = 56;
        const int PlaneOffset = 32;
        const int LightOffsetOffset = 72;
        const int TailOffset = 80;

        Span<byte> standard = stackalloc byte[Size];
        entry.Slice(PlaneOffset, 20).CopyTo(standard);
        entry.Slice(LightOffsetOffset, 8).CopyTo(standard[20..]);
        entry[TailOffset..].CopyTo(standard[28..]);

        return MemoryMarshal.Read<BspFace>(standard);
    }

    public BspFace(in BspFaceWide face)
    {
        PlaneNum = BspWide.ToUInt16(face.PlaneNum);
        Side = face.Side;
        OnNode = face.OnNode;
        FirstEdge = face.FirstEdge;
        NumEdges = BspWide.ToInt16(face.NumEdges);
        TexInfo = BspWide.ToInt16(face.TexInfo);
        DispInfo = BspWide.ToInt16(face.DispInfo);
        FogVolumeId = BspWide.ToInt16(face.FogVolumeId);
        styles = face.Styles;
        LightOffset = face.LightOffset;
        Area = face.Area;
        LightMapOffsetX = face.LightMapOffsetX;
        LightMapOffsetY = face.LightMapOffsetY;
        LightMapSizeX = face.LightMapSizeX;
        LightMapSizeY = face.LightMapSizeY;
        OriginalFace = face.OriginalFace;

        // The lowest bit of the stored value is set on every face and the primitive count follows it
        NumPrimitives = BspWide.ToUInt16(face.NumPrimitives >> 1);
        FirstPrimitive = BspWide.ToUInt16(face.FirstPrimitive);
        SmoothingGroups = face.SmoothingGroups;
    }
}

/// <summary>A face of maps with 32-bit indices (72 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspFaceWide
{
    public readonly int PlaneNum;
    public readonly byte Side;
    public readonly byte OnNode;
    private readonly ushort padding;
    public readonly int FirstEdge;
    public readonly int NumEdges;
    public readonly int TexInfo;
    public readonly int DispInfo;
    public readonly int FogVolumeId;
    public readonly uint Styles;
    public readonly int LightOffset;
    public readonly float Area;
    public readonly int LightMapOffsetX;
    public readonly int LightMapOffsetY;
    public readonly int LightMapSizeX;
    public readonly int LightMapSizeY;
    public readonly int OriginalFace;
    public readonly int NumPrimitives;
    public readonly int FirstPrimitive;
    public readonly uint SmoothingGroups;
}

internal static class BspWide
{
    public static ushort ToUInt16(int value)
    {
        if (value < 0 || value > ushort.MaxValue)
        {
            throw new InvalidDataException($"The value {value} does not fit the 16-bit field it is stored in.");
        }

        return (ushort)value;
    }

    public static short ToInt16(int value)
    {
        if (value < short.MinValue || value > short.MaxValue)
        {
            throw new InvalidDataException($"The value {value} does not fit the 16-bit field it is stored in.");
        }

        return (short)value;
    }
}

internal enum PrimitiveType : ushort
{
    TriangleList,
    TriangleStrip,
    TriangleFan,
}

internal readonly record struct BspPrimitive(PrimitiveType Type, int FirstIndex, int IndexCount, int FirstVert, int VertCount);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspPrimitive16
{
    public readonly PrimitiveType Type;
    public readonly ushort FirstIndex;
    public readonly ushort IndexCount;
    public readonly ushort FirstVert;
    public readonly ushort VertCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspPrimitive32
{
    public readonly int Type;
    public readonly int FirstIndex;
    public readonly int IndexCount;
    public readonly int FirstVert;
    public readonly int VertCount;
}

[Flags]
internal enum SurfFlags
{
    Light = 0x1,
    Sky2D = 0x2,
    Sky = 0x4,
    Warp = 0x8,
    Trans = 0x10,
    NoPortal = 0x20,
    Trigger = 0x40,
    NoDraw = 0x80,
    Hint = 0x100,
    Skip = 0x200,
    NoLight = 0x400,
    BumpLight = 0x800,
    NoShadows = 0x1000,
    NoDecals = 0x2000,
    NoChop = 0x4000,
    Hitbox = 0x8000,
}

/// <summary>How the map was compiled, from the map flags lump.</summary>
[Flags]
internal enum BspMapFlags : uint
{
    None = 0,

    /// <summary>Every light style of a face is followed by an extra block of lightmap data.</summary>
    LightmapAlpha = 0x4,

    /// <summary>The baked lighting of static props has three colors per vertex, one for each bump basis direction.</summary>
    StaticPropLightingThreeColors = 0x8,

    /// <summary>
    /// The extra block holds four bytes per luxel: how much of the flat and each bump basis lightmap the sun lights
    /// directly.
    /// </summary>
    LightmapAlpha3 = 0x10,

    /// <summary>
    /// The baked lighting of static props leaves out the direct sun. The alpha of their colors holds how much the sun
    /// lights each vertex, so it can be added with the sun shadow cascades.
    /// </summary>
    StaticPropLightingWithoutSun = 0x20,

    /// <summary>
    /// Light styles after the first step over the extra block. Without it they are read right after the lightmaps of
    /// the first style, as the game does.
    /// </summary>
    LightStylesWithSunAmount = 0x40,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct TexAxis
{
    public readonly Vector3 Normal;
    public readonly float Offset;

    public float GetCoordinate(Vector3 position) => Vector3.Dot(position, Normal) + Offset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspTextureInfo
{
    public readonly TexAxis TextureUAxis;
    public readonly TexAxis TextureVAxis;
    public readonly TexAxis LightmapUAxis;
    public readonly TexAxis LightmapVAxis;
    public readonly SurfFlags Flags;
    public readonly int TexData;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspTextureData
{
    public readonly Vector3 Reflectivity;
    public readonly int NameStringTableId;
    public readonly int Width;
    public readonly int Height;
    public readonly int ViewWidth;
    public readonly int ViewHeight;
}

internal enum NeighborCorner
{
    LowerLeft,
    UpperLeft,
    UpperRight,
    LowerRight,
}

internal enum NeighborSpan : byte
{
    CornerToCorner,
    CornerToMidpoint,
    MidpointToCorner,
}

internal enum NeighborEdge : byte
{
    Left,
    Top,
    Right,
    Bottom,
}

internal enum NeighborOrientation : byte
{
    CounterClockwise0,
    CounterClockwise90,
    CounterClockwise180,
    CounterClockwise270,
    Unknown = 255,
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 6)]
internal readonly struct DispSubNeighbor
{
    public readonly ushort NeighborIndex;
    public readonly NeighborOrientation NeighborOrientation;
    public readonly NeighborSpan Span;
    public readonly NeighborSpan NeighborSpan;

    public bool IsValid => NeighborIndex != 0xffff;

    public DispSubNeighbor(in DispSubNeighborWide neighbor)
    {
        NeighborIndex = neighbor.NeighborIndex == uint.MaxValue ? (ushort)0xffff : BspWide.ToUInt16((int)neighbor.NeighborIndex);
        NeighborOrientation = neighbor.NeighborOrientation;
        Span = neighbor.Span;
        NeighborSpan = neighbor.NeighborSpan;
    }

    public override string ToString() => IsValid ? $"{NeighborIndex}" : "null";
}

/// <summary>A displacement neighbor of maps with 32-bit indices (8 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
internal readonly struct DispSubNeighborWide
{
    public readonly uint NeighborIndex;
    public readonly NeighborOrientation NeighborOrientation;
    public readonly NeighborSpan Span;
    public readonly NeighborSpan NeighborSpan;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct DispNeighborWide
{
    public readonly DispSubNeighborWide SubNeighbor0;
    public readonly DispSubNeighborWide SubNeighbor1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct DispNeighbor
{
    private readonly DispSubNeighbor subNeighbor0;
    private readonly DispSubNeighbor subNeighbor1;

    public bool Any => subNeighbor0.IsValid || subNeighbor1.IsValid;

    public DispNeighbor(in DispNeighborWide neighbor)
    {
        subNeighbor0 = new DispSubNeighbor(neighbor.SubNeighbor0);
        subNeighbor1 = new DispSubNeighbor(neighbor.SubNeighbor1);
    }

    public DispSubNeighbor this[int index] => index switch
    {
        0 => subNeighbor0,
        1 => subNeighbor1,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public override string ToString() => $"({subNeighbor0}, {subNeighbor1})";
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 10)]
internal readonly struct DispCornerNeighbors
{
    private const int MaxNeighbors = 4;

    private readonly ushort neighbor0;
    private readonly ushort neighbor1;
    private readonly ushort neighbor2;
    private readonly ushort neighbor3;

    public readonly byte NumNeighbors;

    // The entries after the used ones are not initialized
    public DispCornerNeighbors(in DispCornerNeighborsWide neighbors)
    {
        NumNeighbors = (byte)Math.Min((int)neighbors.NumNeighbors, MaxNeighbors);
        neighbor0 = ReadNeighbor(neighbors.Neighbor0, 0, NumNeighbors);
        neighbor1 = ReadNeighbor(neighbors.Neighbor1, 1, NumNeighbors);
        neighbor2 = ReadNeighbor(neighbors.Neighbor2, 2, NumNeighbors);
        neighbor3 = ReadNeighbor(neighbors.Neighbor3, 3, NumNeighbors);
    }

    private static ushort ReadNeighbor(uint value, int index, int count)
        => index < count ? BspWide.ToUInt16((int)value) : (ushort)0;

    public ushort this[int index]
    {
        get
        {
            if (index < 0 || index >= NumNeighbors)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index switch
            {
                0 => neighbor0,
                1 => neighbor1,
                2 => neighbor2,
                _ => neighbor3,
            };
        }
    }
}

/// <summary>The corner neighbors of a displacement in maps with 32-bit indices (20 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 20)]
internal readonly struct DispCornerNeighborsWide
{
    public readonly uint Neighbor0;
    public readonly uint Neighbor1;
    public readonly uint Neighbor2;
    public readonly uint Neighbor3;
    public readonly byte NumNeighbors;
}

/// <summary>A displacement of the DispInfo lump in maps with 32-bit indices (232 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 232)]
internal readonly struct BspDispInfoWide
{
    public readonly Vector3 StartPosition;
    public readonly int DispVertStart;
    public readonly int DispTriStart;
    public readonly int Power;
    public readonly int MinTess;
    public readonly float SmoothingAngle;
    public readonly int Contents;
    public readonly int MapFace;
    public readonly int LightmapAlphaStart;
    public readonly int LightmapSamplePositionStart;

    public readonly DispNeighborWide EdgeNeighbor0;
    public readonly DispNeighborWide EdgeNeighbor1;
    public readonly DispNeighborWide EdgeNeighbor2;
    public readonly DispNeighborWide EdgeNeighbor3;

    public readonly DispCornerNeighborsWide CornerNeighbors0;
    public readonly DispCornerNeighborsWide CornerNeighbors1;
    public readonly DispCornerNeighborsWide CornerNeighbors2;
    public readonly DispCornerNeighborsWide CornerNeighbors3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 176)]
internal readonly struct BspDispInfo
{
    public readonly Vector3 StartPosition;
    public readonly int DispVertStart;
    public readonly int DispTriStart;
    public readonly int Power;
    public readonly int MinTess;
    public readonly float SmoothingAngle;
    public readonly int Contents;
    public readonly ushort MapFace;
    private readonly ushort unused;
    public readonly int LightmapAlphaStart;
    public readonly int LightmapSamplePositionStart;

    private readonly DispNeighbor edgeNeighbor0;
    private readonly DispNeighbor edgeNeighbor1;
    private readonly DispNeighbor edgeNeighbor2;
    private readonly DispNeighbor edgeNeighbor3;

    private readonly DispCornerNeighbors cornerNeighbors0;
    private readonly DispCornerNeighbors cornerNeighbors1;
    private readonly DispCornerNeighbors cornerNeighbors2;
    private readonly DispCornerNeighbors cornerNeighbors3;

    public BspDispInfo(in BspDispInfoV23 info)
    {
        StartPosition = info.StartPosition;
        DispVertStart = info.DispVertStart;
        DispTriStart = info.DispTriStart;
        Power = info.Power;
        MinTess = info.MinTess;
        SmoothingAngle = info.SmoothingAngle;
        Contents = info.Contents;
        MapFace = info.MapFace;
        LightmapAlphaStart = info.LightmapAlphaStart;
        LightmapSamplePositionStart = info.LightmapSamplePositionStart;
        edgeNeighbor0 = info.EdgeNeighbor0;
        edgeNeighbor1 = info.EdgeNeighbor1;
        edgeNeighbor2 = info.EdgeNeighbor2;
        edgeNeighbor3 = info.EdgeNeighbor3;
        cornerNeighbors0 = info.CornerNeighbors0;
        cornerNeighbors1 = info.CornerNeighbors1;
        cornerNeighbors2 = info.CornerNeighbors2;
        cornerNeighbors3 = info.CornerNeighbors3;
    }

    public BspDispInfo(in BspDispInfoWide info)
    {
        StartPosition = info.StartPosition;
        DispVertStart = info.DispVertStart;
        DispTriStart = info.DispTriStart;
        Power = info.Power;
        MinTess = info.MinTess;
        SmoothingAngle = info.SmoothingAngle;
        Contents = info.Contents;
        MapFace = BspWide.ToUInt16(info.MapFace);
        LightmapAlphaStart = info.LightmapAlphaStart;
        LightmapSamplePositionStart = info.LightmapSamplePositionStart;
        edgeNeighbor0 = new DispNeighbor(info.EdgeNeighbor0);
        edgeNeighbor1 = new DispNeighbor(info.EdgeNeighbor1);
        edgeNeighbor2 = new DispNeighbor(info.EdgeNeighbor2);
        edgeNeighbor3 = new DispNeighbor(info.EdgeNeighbor3);
        cornerNeighbors0 = new DispCornerNeighbors(info.CornerNeighbors0);
        cornerNeighbors1 = new DispCornerNeighbors(info.CornerNeighbors1);
        cornerNeighbors2 = new DispCornerNeighbors(info.CornerNeighbors2);
        cornerNeighbors3 = new DispCornerNeighbors(info.CornerNeighbors3);
    }

    public DispNeighbor GetEdgeNeighbor(NeighborEdge edge) => edge switch
    {
        NeighborEdge.Left => edgeNeighbor0,
        NeighborEdge.Top => edgeNeighbor1,
        NeighborEdge.Right => edgeNeighbor2,
        NeighborEdge.Bottom => edgeNeighbor3,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    public DispCornerNeighbors GetCornerNeighbor(NeighborCorner corner) => corner switch
    {
        NeighborCorner.LowerLeft => cornerNeighbors0,
        NeighborCorner.UpperLeft => cornerNeighbors1,
        NeighborCorner.UpperRight => cornerNeighbors2,
        NeighborCorner.LowerRight => cornerNeighbors3,
        _ => throw new ArgumentOutOfRangeException(nameof(corner)),
    };
}

/// <summary>A displacement of the DispInfo lump in version 23 maps (184 bytes). It has two more integers than <see cref="BspDispInfo"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 184)]
internal readonly struct BspDispInfoV23
{
    public readonly Vector3 StartPosition;
    public readonly int DispVertStart;
    public readonly int DispTriStart;
    public readonly int Power;
    public readonly int MinTess;
    public readonly float SmoothingAngle;
    public readonly int Contents;
    public readonly int Reserved0;
    public readonly ushort MapFace;
    private readonly ushort unused;
    public readonly int LightmapAlphaStart;
    public readonly int LightmapSamplePositionStart;
    public readonly int Reserved1;

    public readonly DispNeighbor EdgeNeighbor0;
    public readonly DispNeighbor EdgeNeighbor1;
    public readonly DispNeighbor EdgeNeighbor2;
    public readonly DispNeighbor EdgeNeighbor3;

    public readonly DispCornerNeighbors CornerNeighbors0;
    public readonly DispCornerNeighbors CornerNeighbors1;
    public readonly DispCornerNeighbors CornerNeighbors2;
    public readonly DispCornerNeighbors CornerNeighbors3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspDispVert
{
    public readonly Vector3 Vector;
    public readonly float Distance;
    public readonly float Alpha;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ColorRGBExp32
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly sbyte Exponent;

    private static readonly float[] LuxelScales = CreateScales(255f);

    private static readonly float[] AmbientScales = CreateScales(1f);

    private static float[] CreateScales(float divisor)
    {
        var scales = new float[256];

        for (var i = 0; i < scales.Length; i++)
        {
            var exponent = (sbyte)i;
            scales[i] = MathF.Pow(2f, exponent) / divisor;
        }

        return scales;
    }

    // Indexed by the exponent's byte value, so negative exponents come after the positive ones
    public Vector3 ToLinear() => new Vector3(R, G, B) * LuxelScales[(byte)Exponent];

    // Leaf ambient cubes are not scaled by 1/255 like lightmap luxels
    public Vector3 ToAmbientLinear() => new Vector3(R, G, B) * AmbientScales[(byte)Exponent];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct CubemapSample
{
    public readonly int OriginX;
    public readonly int OriginY;
    public readonly int OriginZ;
    public readonly int Size;

    public Vector3 Origin => new(OriginX, OriginY, OriginZ);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct CompressedLightCube
{
    public readonly ColorRGBExp32 Face0;
    public readonly ColorRGBExp32 Face1;
    public readonly ColorRGBExp32 Face2;
    public readonly ColorRGBExp32 Face3;
    public readonly ColorRGBExp32 Face4;
    public readonly ColorRGBExp32 Face5;

    public ColorRGBExp32 this[int face] => face switch
    {
        0 => Face0,
        1 => Face1,
        2 => Face2,
        3 => Face3,
        4 => Face4,
        5 => Face5,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct LeafAmbientLighting
{
    public readonly CompressedLightCube Cube;
    public readonly byte X;
    public readonly byte Y;
    public readonly byte Z;
    private readonly byte padding;

    public LeafAmbientLighting(CompressedLightCube cube)
    {
        Cube = cube;
        X = 0;
        Y = 0;
        Z = 0;
        padding = 0;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct LeafAmbientIndex
{
    public readonly ushort AmbientSampleCount;
    public readonly ushort FirstAmbientSample;

    public LeafAmbientIndex(ushort ambientSampleCount, ushort firstAmbientSample)
    {
        AmbientSampleCount = ambientSampleCount;
        FirstAmbientSample = firstAmbientSample;
    }
}
