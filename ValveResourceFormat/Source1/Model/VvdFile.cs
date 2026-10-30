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

    public VvdFile(Stream stream)
    {
        stream.Position = 0;

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var id = reader.ReadInt32();
        var version = reader.ReadInt32();

        if (id != 0x56534449 || version != 4)
        {
            throw new InvalidDataException("Unsupported VVD signature or version.");
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
                numVerts[i] = count;
            }
        }

        var numFixups = reader.ReadInt32();
        var fixupTableStart = reader.ReadInt32();
        var vertexDataStart = reader.ReadInt32();
        var tangentDataStart = reader.ReadInt32();

        fixups = numFixups > 0 ? LumpReader<VertexFixup>.ReadLumpAt(stream, fixupTableStart, numFixups) : [];
        Vertices = LumpReader<StudioVertex>.ReadLumpAt(stream, vertexDataStart, numVerts[0]);

        if (tangentDataStart != 0)
        {
            Tangents = LumpReader<Vector4>.ReadLumpAt(stream, tangentDataStart, numVerts[0]);
        }
    }

    public int GetVertexCount(int lod) => numVerts[lod];

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

            for (var i = 0; i < fixup.NumVertices && offset < result.Length; i++)
            {
                result[offset++] = fixup.SourceVertexId + i;
            }
        }

        return result;
    }
}
