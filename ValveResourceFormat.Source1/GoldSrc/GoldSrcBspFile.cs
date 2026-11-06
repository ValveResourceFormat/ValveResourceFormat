using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// A version 30 BSP map.
/// </summary>
internal sealed class GoldSrcBspFile
{
    public const int SupportedVersion = 30;
    public const int LumpCount = 15;
    private const int HeaderSize = 4 + (LumpCount * 8);

    private readonly byte[] data;
    private readonly (int Offset, int Length)[] lumps = new (int, int)[LumpCount];

    private List<BspEntity>? entities;
    private GoldSrcPlane[]? planes;
    private Vector3[]? vertices;
    private GoldSrcTextureInfo[]? textureInfos;
    private GoldSrcFace[]? faces;
    private GoldSrcNode[]? nodes;
    private GoldSrcLeaf[]? leafs;
    private GoldSrcClipNode[]? clipNodes;
    private GoldSrcModel[]? models;
    private GoldSrcEdge[]? edges;
    private int[]? surfaceEdges;
    private (PalettedImage?[] Images, string[] Names)? textureLump;
    private string[]? wadNames;
    private GoldSrcLightmapExtents[]? lightmapExtents;

    public string Name { get; }
    public int Version { get; }
    public int FileLength => data.Length;

    public GoldSrcBspFile(byte[] data, string name)
    {
        this.data = data;
        Name = name;

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too small to be a BSP.");
        }

        Version = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (Version != SupportedVersion)
        {
            throw new UnexpectedMagicException($"Unsupported GoldSrc BSP version, supported version is {SupportedVersion}", Version, nameof(Version));
        }

        for (var i = 0; i < LumpCount; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 + (i * 8)));
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8 + (i * 8)));

            if (offset < 0 || length < 0 || offset > data.Length - length)
            {
                throw new InvalidDataException($"Lump {(GoldSrcLump)i} is out of range.");
            }

            lumps[i] = (offset, length);
        }

        if (HasSwappedEntityAndPlaneLumps())
        {
            var entityLump = lumps[(int)GoldSrcLump.Entities];
            lumps[(int)GoldSrcLump.Entities] = lumps[(int)GoldSrcLump.Planes];
            lumps[(int)GoldSrcLump.Planes] = entityLump;
        }
    }

    private bool HasSwappedEntityAndPlaneLumps()
    {
        const int PlaneSize = 20;

        var (_, entityLength) = lumps[(int)GoldSrcLump.Entities];
        var (planeOffset, planeLength) = lumps[(int)GoldSrcLump.Planes];

        return planeLength > 0 && data[planeOffset] == '{' && entityLength % PlaneSize == 0;
    }

    /// <summary>
    /// Whether the file starts like a GoldSrc BSP. Source 1 maps start with an identifier instead of the version.
    /// </summary>
    public static bool IsGoldSrcBsp(ReadOnlySpan<byte> header) => header.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(header) == SupportedVersion;

    public static GoldSrcBspFile Open(Stream stream, string name) => new(Source1Streams.ReadAll(stream), name);

    public ReadOnlySpan<byte> GetLumpData(GoldSrcLump lump)
    {
        var (offset, length) = lumps[(int)lump];
        return data.AsSpan(offset, length);
    }

    public IReadOnlyList<BspEntity> Entities => entities ??= BspEntity.Parse(GetLumpData(GoldSrcLump.Entities));
    public GoldSrcPlane[] Planes => planes ??= ReadLump<GoldSrcPlane>(GoldSrcLump.Planes);
    public Vector3[] Vertices => vertices ??= ReadLump<Vector3>(GoldSrcLump.Vertices);
    public GoldSrcTextureInfo[] TextureInfos => textureInfos ??= ReadLump<GoldSrcTextureInfo>(GoldSrcLump.TexInfo);
    public GoldSrcFace[] Faces => faces ??= ReadLump<GoldSrcFace>(GoldSrcLump.Faces);
    public GoldSrcNode[] Nodes => nodes ??= ReadLump<GoldSrcNode>(GoldSrcLump.Nodes);
    public GoldSrcLeaf[] Leafs => leafs ??= ReadLump<GoldSrcLeaf>(GoldSrcLump.Leafs);
    public GoldSrcClipNode[] ClipNodes => clipNodes ??= ReadLump<GoldSrcClipNode>(GoldSrcLump.ClipNodes);
    public GoldSrcModel[] Models => models ??= ReadLump<GoldSrcModel>(GoldSrcLump.Models);
    public GoldSrcEdge[] Edges => edges ??= ReadLump<GoldSrcEdge>(GoldSrcLump.Edges);
    public int[] SurfaceEdges => surfaceEdges ??= ReadLump<int>(GoldSrcLump.SurfEdges);

    /// <summary>The raw lightmap samples, three bytes per luxel.</summary>
    public ReadOnlySpan<byte> Lighting => GetLumpData(GoldSrcLump.Lighting);

    public ReadOnlySpan<byte> Visibility => GetLumpData(GoldSrcLump.Visibility);

    /// <summary>
    /// The textures of the texture lump. An entry is null when the texture is not embedded and comes from a WAD.
    /// </summary>
    public IReadOnlyList<PalettedImage?> Textures => TextureLump.Images;

    /// <summary>The names of all texture lump entries, including those without embedded pixels.</summary>
    public IReadOnlyList<string> TextureNames => TextureLump.Names;

    /// <summary>The WAD file names listed by the worldspawn <c>wad</c> key, without folders.</summary>
    public IReadOnlyList<string> WadNames => wadNames ??= ReadWadNames();

    private (PalettedImage?[] Images, string[] Names) TextureLump => textureLump ??= ReadTextures();

    private T[] ReadLump<T>(GoldSrcLump lump) where T : struct
    {
        var (offset, length) = lumps[(int)lump];
        return LumpReader<T>.ReadLump(data, offset, length);
    }

    private string[] ReadWadNames()
    {
        var list = Entities.Count > 0 ? Entities[0].GetString("wad") : string.Empty;
        var names = new List<string>();

        foreach (var entry in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.LastIndexOfAny(['/', '\\']);
            names.Add(separator < 0 ? entry : entry[(separator + 1)..]);
        }

        return [.. names];
    }

    /// <summary>
    /// Finds the image of a texture of the texture lump: the embedded one, or else the first one a WAD has.
    /// </summary>
    /// <param name="textureIndex">The index of the texture in the texture lump.</param>
    /// <param name="findInWads">Looks a texture name up in the WADs of the map, which are given in the order they are listed. Null when there are no WADs to search.</param>
    /// <returns>The image, or null when no source has it.</returns>
    public PalettedImage? ResolveTexture(int textureIndex, Func<string, IReadOnlyList<string>, PalettedImage?>? findInWads)
    {
        var embedded = Textures[textureIndex];

        if (embedded != null || findInWads == null)
        {
            return embedded;
        }

        var name = TextureNames[textureIndex];

        if (name.Length == 0)
        {
            return null;
        }

        return findInWads(name, WadNames);
    }

    private (PalettedImage?[] Images, string[] Names) ReadTextures()
    {
        var lump = GetLumpData(GoldSrcLump.Textures);

        if (lump.Length < 4)
        {
            return ([], []);
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(lump);

        if (count < 0 || count > (lump.Length - 4) / 4)
        {
            throw new InvalidDataException("Texture lump has an invalid count.");
        }

        var result = new PalettedImage?[count];
        var names = new string[count];

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(lump[(4 + (i * 4))..]);

            if (offset < 0)
            {
                names[i] = string.Empty;
                continue;
            }

            if (offset > lump.Length - 16)
            {
                throw new InvalidDataException($"Texture {i} is out of range.");
            }

            var texture = lump[offset..];
            names[i] = PalettedImage.ReadName(texture[..16]);
            result[i] = PalettedImage.ReadMipTexture(texture);
        }

        return (result, names);
    }

    /// <summary>
    /// Where the lightmap of each face is in luxel space, by face index: the lowest luxel coordinates in 16 unit
    /// steps, and the size in luxels, which is the extent of the texture coordinates of the face in 16 unit steps plus one.
    /// A face without a valid texture info or edges has an empty extent.
    /// </summary>
    public IReadOnlyList<GoldSrcLightmapExtents> LightmapExtents => lightmapExtents ??= ReadLightmapExtents();

    private GoldSrcLightmapExtents[] ReadLightmapExtents()
    {
        var faces = Faces;
        var result = new GoldSrcLightmapExtents[faces.Length];

        for (var i = 0; i < faces.Length; i++)
        {
            var face = faces[i];

            if (face.TexInfo >= 0 && face.TexInfo < TextureInfos.Length && face.NumEdges > 0)
            {
                result[i] = GetLightmapExtents(face);
            }
        }

        return result;
    }

    private GoldSrcLightmapExtents GetLightmapExtents(in GoldSrcFace face)
    {
        var info = TextureInfos[face.TexInfo];
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        for (var i = 0; i < face.NumEdges; i++)
        {
            var coordinates = info.GetTextureCoordinates(GetFaceVertex(face, i));
            min = Vector2.Min(min, coordinates);
            max = Vector2.Max(max, coordinates);
        }

        var minS = (int)MathF.Floor(min.X / 16f);
        var minT = (int)MathF.Floor(min.Y / 16f);
        var maxS = (int)MathF.Ceiling(max.X / 16f);
        var maxT = (int)MathF.Ceiling(max.Y / 16f);

        return new GoldSrcLightmapExtents(minS, minT, maxS - minS + 1, maxT - minT + 1);
    }

    /// <summary>
    /// Gets the vertices of a face in winding order.
    /// </summary>
    public Vector3[] GetFaceVertices(in GoldSrcFace face)
    {
        var result = new Vector3[face.NumEdges];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = GetFaceVertex(face, i);
        }

        return result;
    }

    private Vector3 GetFaceVertex(in GoldSrcFace face, int index)
    {
        var allSurfaceEdges = SurfaceEdges;
        var surfaceEdgeIndex = (long)face.FirstEdge + index;

        if (face.FirstEdge < 0 || surfaceEdgeIndex >= allSurfaceEdges.Length)
        {
            throw new InvalidDataException("Face has surface edges out of range.");
        }

        var surfaceEdge = allSurfaceEdges[surfaceEdgeIndex];
        var edgeIndex = Math.Abs((long)surfaceEdge);
        var allEdges = Edges;

        if (edgeIndex >= allEdges.Length)
        {
            throw new InvalidDataException("Face has an edge out of range.");
        }

        var edge = allEdges[edgeIndex];
        var vertexIndex = surfaceEdge >= 0 ? edge.A : edge.B;
        var allVertices = Vertices;

        if (vertexIndex >= allVertices.Length)
        {
            throw new InvalidDataException("Edge has a vertex out of range.");
        }

        return allVertices[vertexIndex];
    }
}

/// <summary>The lightmap of a face in luxel space.</summary>
/// <param name="MinS">The lowest luxel coordinate along the S axis, in 16 unit steps.</param>
/// <param name="MinT">The lowest luxel coordinate along the T axis, in 16 unit steps.</param>
/// <param name="Width">The width in luxels.</param>
/// <param name="Height">The height in luxels.</param>
internal readonly record struct GoldSrcLightmapExtents(int MinS, int MinT, int Width, int Height);

/// <summary>The lump table order of a version 30 BSP.</summary>
internal enum GoldSrcLump
{
    Entities,
    Planes,
    Textures,
    Vertices,
    Visibility,
    Nodes,
    TexInfo,
    Faces,
    Lighting,
    ClipNodes,
    Leafs,
    MarkSurfaces,
    Edges,
    SurfEdges,
    Models,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcPlane
{
    public readonly Vector3 Normal;
    public readonly float Dist;
    public readonly int Type;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcNode
{
    public readonly int PlaneNum;

    /// <summary>The children, where a negative value is the bitwise complement of a leaf index.</summary>
    public readonly short Front;
    public readonly short Back;
    public readonly short MinX;
    public readonly short MinY;
    public readonly short MinZ;
    public readonly short MaxX;
    public readonly short MaxY;
    public readonly short MaxZ;
    public readonly ushort FirstFace;
    public readonly ushort NumFaces;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcEdge
{
    public readonly ushort A;
    public readonly ushort B;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcTextureInfo
{
    public readonly Vector3 S;
    public readonly float SOffset;
    public readonly Vector3 T;
    public readonly float TOffset;
    public readonly int MipTexture;
    public readonly int Flags;

    /// <summary>Whether the surface is drawn without a lightmap, as for sky and liquids.</summary>
    public bool IsSpecial => (Flags & 1) != 0;

    public Vector2 GetTextureCoordinates(Vector3 position)
    {
        return new Vector2(Vector3.Dot(position, S) + SOffset, Vector3.Dot(position, T) + TOffset);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcFace
{
    public readonly ushort PlaneNum;
    public readonly short Side;
    public readonly int FirstEdge;
    public readonly short NumEdges;
    public readonly short TexInfo;
    private readonly uint styles;
    public readonly int LightOffset;

    public bool HasLightmap => LightOffset != -1;

    public byte GetLightStyle(int index) => (byte)((styles >> (index << 3)) & 0xff);

    /// <summary>The number of lightmap copies stored for the face, one per light style slot in use.</summary>
    public int LightStyleCount
    {
        get
        {
            var count = 0;

            while (count < 4 && GetLightStyle(count) != 255)
            {
                count++;
            }

            return count;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcLeaf
{
    public readonly int Contents;
    public readonly int VisibilityOffset;
    public readonly short MinX;
    public readonly short MinY;
    public readonly short MinZ;
    public readonly short MaxX;
    public readonly short MaxY;
    public readonly short MaxZ;
    public readonly ushort FirstMarkSurface;
    public readonly ushort NumMarkSurfaces;
    public readonly uint AmbientLevels;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcModel
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;
    public readonly Vector3 Origin;
    public readonly int HeadNode0;
    public readonly int HeadNode1;
    public readonly int HeadNode2;
    public readonly int HeadNode3;
    public readonly int VisibilityLeafs;
    public readonly int FirstFace;
    public readonly int NumFaces;
}

/// <summary>A node of a collision hull, whose children are clip node indices or negative contents.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct GoldSrcClipNode
{
    public readonly int PlaneNum;
    public readonly short Front;
    public readonly short Back;
}

/// <summary>The contents of a leaf or of the empty end of a collision hull.</summary>
internal static class GoldSrcContents
{
    public const int Empty = -1;
    public const int Solid = -2;
    public const int Water = -3;
    public const int Slime = -4;
    public const int Lava = -5;
    public const int Sky = -6;
    public const int CurrentFirst = -9;
    public const int CurrentLast = -14;
}
