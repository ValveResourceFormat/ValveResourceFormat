using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// The baked per vertex lighting of a static prop, stored in the pakfile of a map as <c>sp_N.vhv</c>
/// and <c>sp_hdr_N.vhv</c>. It holds one block of colors per strip group of the model.
/// </summary>
internal sealed class VhvFile
{
    private const int HeaderSize = 40;
    private const int MeshHeaderSize = 32;
    private const int BytesPerVertex = 4;

    // Vertex lighting is stored as pow(linear, 1 / 2.2) / 2, so it covers linear values from 0 to 4
    private const float Gamma = 2.2f;
    private const float Overbright = 2f;

    private static readonly float[] ChannelValues = CreateChannelValues();

    private readonly byte[][] stripGroups;

    /// <summary>Gets the checksum of the model the lighting was baked for.</summary>
    public int Checksum { get; }

    private VhvFile(int checksum, byte[][] stripGroups)
    {
        Checksum = checksum;
        this.stripGroups = stripGroups;
    }

    /// <summary>Reads the LOD 0 strip groups of a file from the rest of a stream.</summary>
    /// <param name="stream">The stream of the file.</param>
    /// <returns>The file, or null when it is not a per vertex color file.</returns>
    public static VhvFile? Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Parse(Source1Model.ReadAll(stream));
    }

    /// <summary>Reads the LOD 0 strip groups of a file.</summary>
    /// <param name="data">The file contents.</param>
    /// <returns>The file, or null when it is not a per vertex color file.</returns>
    public static VhvFile? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            return null;
        }

        var checksum = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var vertexSize = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var meshCount = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);

        if (vertexSize != BytesPerVertex || meshCount < 0 || (long)meshCount * MeshHeaderSize > data.Length - HeaderSize)
        {
            return null;
        }

        var groups = new List<byte[]>();

        for (var i = 0; i < meshCount; i++)
        {
            var header = data[(HeaderSize + (i * MeshHeaderSize))..];
            var lod = BinaryPrimitives.ReadInt32LittleEndian(header);
            var vertexCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);

            if (vertexCount < 0 || offset < 0 || (long)vertexCount * BytesPerVertex > data.Length - offset)
            {
                return null;
            }

            if (lod == 0)
            {
                groups.Add(data.Slice(offset, vertexCount * BytesPerVertex).ToArray());
            }
        }

        return new VhvFile(checksum, [.. groups]);
    }

    /// <summary>Gets the linear light baked into the vertices of a mesh.</summary>
    /// <param name="mesh">A mesh of the model the file was baked for.</param>
    /// <returns>A color for each of <see cref="Source1Mesh.Vertices"/>, or null when the file does not match the mesh.</returns>
    public Vector3[]? GetVertexLighting(Source1Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var outputByOriginal = mesh.OutputByOriginalVertexId;
        var colors = new Vector3[mesh.Vertices.Length];

        for (var group = 0; group < mesh.StripGroupVertexIds.Length; group++)
        {
            var groupIndex = mesh.FirstStripGroup + group;
            var vertexIds = mesh.StripGroupVertexIds[group];

            if (groupIndex >= stripGroups.Length || stripGroups[groupIndex].Length != vertexIds.Length * BytesPerVertex)
            {
                return null;
            }

            var bytes = stripGroups[groupIndex];

            for (var i = 0; i < vertexIds.Length; i++)
            {
                var original = vertexIds[i];

                if ((uint)original < (uint)outputByOriginal.Length && outputByOriginal[original] >= 0)
                {
                    colors[outputByOriginal[original]] = DecodeColor(bytes.AsSpan(i * BytesPerVertex, BytesPerVertex));
                }
            }
        }

        return colors;
    }

    // Bytes are blue, green, red, alpha
    private static Vector3 DecodeColor(ReadOnlySpan<byte> bgra)
    {
        return new Vector3(DecodeChannel(bgra[2]), DecodeChannel(bgra[1]), DecodeChannel(bgra[0]));
    }

    private static float DecodeChannel(byte value) => ChannelValues[value];

    private static float[] CreateChannelValues()
    {
        var values = new float[byte.MaxValue + 1];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = MathF.Pow(i / 255f * Overbright, Gamma);
        }

        return values;
    }
}
