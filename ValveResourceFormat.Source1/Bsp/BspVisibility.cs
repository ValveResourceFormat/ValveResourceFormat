using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>The Visibility lump: per cluster run-length encoded bit sets of the clusters that can be seen (PVS) and heard (PAS).</summary>
internal sealed class BspVisibility
{
    private const int HeaderSize = 4;
    private const int ClusterEntrySize = 8;

    private readonly ReadOnlyMemory<byte> data;
    private readonly byte[]?[] decodedPvs;

    public int ClusterCount { get; }

    public BspVisibility(ReadOnlyMemory<byte> data)
    {
        this.data = data;

        if (data.Length >= HeaderSize)
        {
            var count = BinaryPrimitives.ReadInt32LittleEndian(data.Span);

            if (count < 0 || (long)HeaderSize + ((long)count * ClusterEntrySize) > data.Length)
            {
                throw new InvalidDataException("Visibility lump is too small for its cluster count.");
            }

            ClusterCount = count;
        }

        decodedPvs = new byte[ClusterCount][];
    }

    /// <summary>The byte offset of the encoded PVS of a cluster, from the start of the lump.</summary>
    public int GetPvsOffset(int cluster) => ReadOffset(cluster, 0);

    /// <summary>The byte offset of the encoded PAS of a cluster, from the start of the lump.</summary>
    public int GetPasOffset(int cluster) => ReadOffset(cluster, 1);

    /// <summary>Decodes the PVS of a cluster into a bit set of <see cref="ClusterCount"/> bits.</summary>
    public byte[] DecodePvs(int cluster) => decodedPvs[cluster] ??= Decode(GetPvsOffset(cluster));

    /// <summary>Decodes the PAS of a cluster into a bit set of <see cref="ClusterCount"/> bits.</summary>
    public byte[] DecodePas(int cluster) => Decode(GetPasOffset(cluster));

    /// <summary>Whether cluster <paramref name="to"/> is in the PVS of cluster <paramref name="from"/>. Clusters outside the map, such as -1 for solid leaves, see and are seen by nothing.</summary>
    public bool IsVisible(int from, int to)
    {
        if (from < 0 || from >= ClusterCount || to < 0 || to >= ClusterCount)
        {
            return false;
        }

        var bits = DecodePvs(from);
        return (bits[to >> 3] & (1 << (to & 7))) != 0;
    }

    private int ReadOffset(int cluster, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cluster);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cluster, ClusterCount);

        return BinaryPrimitives.ReadInt32LittleEndian(data.Span[(HeaderSize + (cluster * ClusterEntrySize) + (index * 4))..]);
    }

    private byte[] Decode(int offset) => DecodeRow(data.Span, offset, (ClusterCount + 7) / 8);

    /// <summary>Decodes a run-length encoded bit set, such as a PVS row.</summary>
    /// <param name="span">The data the offset points into.</param>
    /// <param name="offset">The byte offset of the encoded row.</param>
    /// <param name="rowBytes">The number of bytes of the decoded row.</param>
    internal static byte[] DecodeRow(ReadOnlySpan<byte> span, int offset, int rowBytes)
    {
        var output = new byte[rowBytes];

        if (offset < 0 || offset > span.Length)
        {
            throw new InvalidDataException("Visibility offset lies outside the lump.");
        }

        var input = offset;
        var written = 0;

        while (written < output.Length && input < span.Length)
        {
            var value = span[input++];

            if (value != 0)
            {
                output[written++] = value;
                continue;
            }

            if (input >= span.Length)
            {
                break;
            }

            // A zero byte is followed by the number of zero bytes it stands for
            written += span[input++];
        }

        return output;
    }
}
