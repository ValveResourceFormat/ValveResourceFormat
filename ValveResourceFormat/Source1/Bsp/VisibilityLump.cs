using System.Buffers.Binary;

namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class BspVisibility
    {
        private readonly ReadOnlyMemory<byte> lump;
        private readonly int[] pvsOffsets = [];
        private readonly Dictionary<int, HashSet<int>> cache = [];

        public int NumClusters { get; }

        internal BspVisibility(ReadOnlyMemory<byte> lump)
        {
            this.lump = lump;

            var span = lump.Span;

            if (span.Length < 4)
            {
                return;
            }

            var clusters = BinaryPrimitives.ReadInt32LittleEndian(span);
            clusters = Math.Clamp(clusters, 0, (span.Length - 4) / 8);

            NumClusters = clusters;
            pvsOffsets = new int[clusters];

            for (var i = 0; i < clusters; i++)
            {
                pvsOffsets[i] = BinaryPrimitives.ReadInt32LittleEndian(span[(4 + (i * 8))..]);
            }
        }

        public HashSet<int> GetVisibleClusters(int cluster)
        {
            if (cache.TryGetValue(cluster, out var cached))
            {
                return cached;
            }

            var set = new HashSet<int>();
            var span = lump.Span;
            var position = pvsOffsets[cluster];
            var offset = 0;

            while (offset < NumClusters && position < span.Length)
            {
                var bits = span[position++];

                if (bits == 0)
                {
                    if (position >= span.Length)
                    {
                        break;
                    }

                    offset += span[position++] * 8;
                    continue;
                }

                for (var i = 0; i < 8 && offset + i < NumClusters; i++)
                {
                    if ((bits & (1 << i)) != 0)
                    {
                        set.Add(offset + i);
                    }
                }

                offset += 8;
            }

            cache[cluster] = set;
            return set;
        }
    }
}
