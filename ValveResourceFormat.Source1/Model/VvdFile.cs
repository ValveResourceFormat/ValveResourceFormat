using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct StudioVertex
{
    public StudioBoneWeight BoneWeights;
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct StudioBoneWeight
{
    public float Weight0;
    public float Weight1;
    public float Weight2;
    public byte Bone0;
    public byte Bone1;
    public byte Bone2;
    public byte NumBones;
}

internal sealed class VvdFile
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct VertexFixup
    {
        public int Lod;
        public int SourceVertexId;
        public int NumVertices;
    }

    private readonly int[] numVerts;
    private readonly VertexFixup[] fixups;

    public int Checksum { get; }
    public int NumLods { get; }
    public StudioVertex[] Vertices { get; }
    public Vector4[]? Tangents { get; }

    /// <summary>The second set of texture coordinates of each vertex, or <see langword="null"/> when the model has one set.</summary>
    public Vector2[]? TexCoords2 { get; }

    public VvdFile(Stream stream)
    {
        stream.Position = 0;

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var id = reader.ReadInt32();
        var version = reader.ReadInt32();

        if (id != 0x56534449)
        {
            throw new UnexpectedMagicException("Not a VVD file", id, nameof(id));
        }

        if (version != 4)
        {
            throw new UnexpectedMagicException("Unsupported VVD version, supported version is 4", version, nameof(version));
        }

        Checksum = reader.ReadInt32();
        NumLods = reader.ReadInt32();

        if (NumLods < 1 || NumLods > 8)
        {
            throw new InvalidDataException("Invalid VVD LOD count.");
        }

        numVerts = new int[NumLods];

        for (var i = 0; i < 8; i++)
        {
            var count = reader.ReadInt32();

            if (i < NumLods)
            {
                if (count < 0)
                {
                    throw new InvalidDataException("Invalid VVD vertex count.");
                }

                numVerts[i] = count;
            }
        }

        var numFixups = reader.ReadInt32();
        var fixupTableStart = reader.ReadInt32();
        var vertexDataStart = reader.ReadInt32();
        var tangentDataStart = reader.ReadInt32();

        fixups = numFixups > 0 ? LumpReader<VertexFixup>.ReadLumpAt(stream, fixupTableStart, numFixups) : [];
        Vertices = LumpReader<StudioVertex>.ReadLumpAt(stream, vertexDataStart, numVerts[0]);

        var dataEnd = vertexDataStart + (long)numVerts[0] * Marshal.SizeOf<StudioVertex>();

        if (tangentDataStart != 0)
        {
            Tangents = LumpReader<Vector4>.ReadLumpAt(stream, tangentDataStart, numVerts[0]);
            dataEnd = Math.Max(dataEnd, tangentDataStart + (long)numVerts[0] * Marshal.SizeOf<Vector4>());
        }

        TexCoords2 = ReadSecondTexCoords(stream, reader, dataEnd, numVerts[0]);
    }

    // Models with a second texture coordinate set carry a small header and one Vector2 per vertex after the tangents
    private static Vector2[]? ReadSecondTexCoords(Stream stream, BinaryReader reader, long start, int count)
    {
        const int HeaderSize = 20;

        if (count == 0 || stream.Length - start != HeaderSize + (long)count * Marshal.SizeOf<Vector2>())
        {
            return null;
        }

        stream.Position = start;

        _ = reader.ReadInt32();
        var size = reader.ReadInt32();
        _ = reader.ReadInt32();
        var dataStart = reader.ReadInt32();
        var elementSize = reader.ReadInt32();

        if (size != stream.Length - start || dataStart != HeaderSize || elementSize != Marshal.SizeOf<Vector2>())
        {
            return null;
        }

        return LumpReader<Vector2>.ReadLumpAt(stream, start + dataStart, count);
    }

    public int[]? GetVertexIndices(int lod)
    {
        if (fixups.Length == 0)
        {
            return null;
        }

        var result = new int[numVerts[lod]];
        var offset = 0;

        foreach (var fixup in fixups)
        {
            if (fixup.Lod < lod)
            {
                continue;
            }

            if (fixup.SourceVertexId < 0 || fixup.NumVertices < 0 || fixup.SourceVertexId > Vertices.Length - fixup.NumVertices)
            {
                throw new InvalidDataException("VVD fixup is outside the vertices.");
            }

            for (var i = 0; i < fixup.NumVertices && offset < result.Length; i++)
            {
                result[offset++] = fixup.SourceVertexId + i;
            }
        }

        return result;
    }
}
