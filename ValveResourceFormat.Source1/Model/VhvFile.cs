using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// The baked per vertex lighting of a static prop, stored in the pakfile of a map as <c>sp_N.vhv</c>
/// and <c>sp_hdr_N.vhv</c>. It holds one block of colors per strip group of the model, with one color per vertex,
/// or three when the map was lit for bump mapped props.
/// </summary>
internal sealed class VhvFile
{
    private const int SupportedVersion = 2;
    private const int HeaderSize = 40;
    private const int MeshHeaderSize = 28;
    private const int BytesPerColor = 4;

    // Vertex lighting is stored as pow(linear, 1 / 2.2) / 2, so it covers linear values from 0 to 4
    private const float Gamma = 2.2f;
    private const float Overbright = 2f;

    private static readonly float[] ChannelValues = CreateChannelValues();

    // The most levels of detail a model has
    private const int MaxLods = 8;

    private readonly byte[][][] stripGroups;
    private readonly int colorsPerVertex;

    /// <summary>Gets the checksum of the model the lighting was baked for.</summary>
    public int Checksum { get; }

    private VhvFile(int checksum, int colorsPerVertex, byte[][][] stripGroups)
    {
        Checksum = checksum;
        this.colorsPerVertex = colorsPerVertex;
        this.stripGroups = stripGroups;
    }

    private int BytesPerVertex => colorsPerVertex * BytesPerColor;

    /// <summary>Reads the strip groups of a file from the rest of a stream.</summary>
    /// <param name="stream">The stream of the file.</param>
    /// <param name="colorsPerVertex">How many colors the map baked for each vertex, one or three.</param>
    /// <returns>The file, or null when it is not a per vertex color file with that many colors.</returns>
    public static VhvFile? Parse(Stream stream, int colorsPerVertex)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Parse(Source1Streams.ReadAll(stream), colorsPerVertex);
    }

    /// <summary>Reads the strip groups of a file.</summary>
    /// <param name="data">The file contents.</param>
    /// <param name="colorsPerVertex">How many colors the map baked for each vertex, one or three.</param>
    /// <returns>The file, or null when it is not a per vertex color file with that many colors.</returns>
    /// <exception cref="UnexpectedMagicException">The version of the file is not supported.</exception>
    public static VhvFile? Parse(ReadOnlySpan<byte> data, int colorsPerVertex)
    {
        if (data.Length < HeaderSize)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (version != SupportedVersion)
        {
            throw new UnexpectedMagicException($"Unsupported VHV version, supported version is {SupportedVersion}", version, nameof(version));
        }

        var checksum = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var vertexSize = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var meshCount = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);

        if (vertexSize != colorsPerVertex * BytesPerColor || meshCount < 0 || (long)meshCount * MeshHeaderSize > data.Length - HeaderSize)
        {
            return null;
        }

        var groups = new List<byte[]>[MaxLods];

        for (var i = 0; i < groups.Length; i++)
        {
            groups[i] = [];
        }

        for (var i = 0; i < meshCount; i++)
        {
            var header = data[(HeaderSize + (i * MeshHeaderSize))..];
            var lod = BinaryPrimitives.ReadInt32LittleEndian(header);
            var vertexCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);

            if (vertexCount < 0 || offset < 0 || (long)vertexCount * vertexSize > data.Length - offset)
            {
                return null;
            }

            if ((uint)lod < (uint)groups.Length)
            {
                groups[lod].Add(data.Slice(offset, vertexCount * vertexSize).ToArray());
            }
        }

        var stripGroups = new byte[groups.Length][][];

        for (var i = 0; i < stripGroups.Length; i++)
        {
            stripGroups[i] = [.. groups[i]];
        }

        return new VhvFile(checksum, colorsPerVertex, stripGroups);
    }

    /// <summary>Gets the linear light baked into the vertices of a mesh.</summary>
    /// <param name="mesh">A mesh of the model the file was baked for.</param>
    /// <param name="withSunAmount">Whether to keep how much the sun lights each vertex directly in the alpha, which is zero otherwise.</param>
    /// <returns>A color for each of <see cref="Source1Mesh.Vertices"/>, or null when the file does not match the mesh.</returns>
    public Vector4[]? GetVertexLighting(Source1Mesh mesh, bool withSunAmount)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if ((uint)mesh.Lod >= (uint)stripGroups.Length)
        {
            return null;
        }

        var lodStripGroups = stripGroups[mesh.Lod];
        var outputByOriginal = mesh.OutputByOriginalVertexId;
        var colors = new Vector4[mesh.Vertices.Length];

        for (var group = 0; group < mesh.StripGroupVertexIds.Length; group++)
        {
            var groupIndex = mesh.FirstStripGroup + group;
            var vertexIds = mesh.StripGroupVertexIds[group];

            if (groupIndex >= lodStripGroups.Length || lodStripGroups[groupIndex].Length != vertexIds.Length * BytesPerVertex)
            {
                return null;
            }

            var bytes = lodStripGroups[groupIndex];

            for (var i = 0; i < vertexIds.Length; i++)
            {
                var original = vertexIds[i];

                if ((uint)original < (uint)outputByOriginal.Length && outputByOriginal[original] >= 0)
                {
                    var color = DecodeVertex(bytes.AsSpan(i * BytesPerVertex, BytesPerVertex));

                    if (!withSunAmount)
                    {
                        color.W = 0f;
                    }

                    colors[outputByOriginal[original]] = color;
                }
            }
        }

        return colors;
    }

    // The three colors of a vertex lit for bump mapping are averaged, as the game does for props that are not bump mapped
    private static Vector4 DecodeVertex(ReadOnlySpan<byte> vertex)
    {
        var sum = Vector4.Zero;
        var count = vertex.Length / BytesPerColor;

        for (var i = 0; i < count; i++)
        {
            sum += DecodeColor(vertex.Slice(i * BytesPerColor, BytesPerColor));
        }

        return sum / count;
    }

    // Bytes are blue, green, red, then the sun amount, which is linear
    private static Vector4 DecodeColor(ReadOnlySpan<byte> bgra)
    {
        return new Vector4(DecodeChannel(bgra[2]), DecodeChannel(bgra[1]), DecodeChannel(bgra[0]), bgra[3] / 255f);
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
