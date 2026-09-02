using System.Linq;
namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Pairs a mesh's tools buffers with the render vertex buffers they augment, matching on element
    /// count and consuming each tools buffer at most once.
    /// </summary>
    public sealed class ToolsBufferMatcher
    {
        private readonly IReadOnlyList<VBIB.OnDiskBufferData> toolsBuffers;
        private readonly bool[] claimed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolsBufferMatcher"/> class over a mesh's buffers.
        /// </summary>
        public ToolsBufferMatcher(VBIB vbib)
        {
            ArgumentNullException.ThrowIfNull(vbib);

            toolsBuffers = vbib.ToolsBuffers;
            claimed = new bool[toolsBuffers.Count];
        }

        /// <summary>
        /// Gets whether the mesh carries no tools buffers at all.
        /// </summary>
        public bool IsEmpty => toolsBuffers.Count == 0;

        /// <summary>
        /// Claims the first unclaimed tools buffer describing the given number of vertices, or returns
        /// <see langword="null"/> when none is left.
        /// </summary>
        public VBIB.OnDiskBufferData? TryClaim(uint elementCount)
        {
            var index = Find(elementCount, null);

            if (index < 0)
            {
                return null;
            }

            claimed[index] = true;
            return toolsBuffers[index];
        }

        /// <summary>
        /// Claims and concatenates the tools buffer matching each of the given, already order-matched
        /// vertex buffers. Returns <see langword="null"/>, claiming nothing, unless every one of them has
        /// a tools buffer left to claim and those tools buffers share a layout.
        /// </summary>
        public VBIB.OnDiskBufferData? TryClaimMerged(IReadOnlyList<VBIB.OnDiskBufferData> vertexBuffers)
        {
            ArgumentNullException.ThrowIfNull(vertexBuffers);

            if (IsEmpty)
            {
                return null;
            }

            var picked = new List<int>(vertexBuffers.Count);
            var pending = new bool[toolsBuffers.Count];

            foreach (var vertexBuffer in vertexBuffers)
            {
                var index = Find(vertexBuffer.ElementCount, pending);

                if (index < 0)
                {
                    return null;
                }

                pending[index] = true;
                picked.Add(index);
            }

            var matched = picked.Select(index => toolsBuffers[index]).ToList();

            if (matched.Exists(buffer => !VBIB.HasSameLayout(matched[0], buffer)))
            {
                return null;
            }

            foreach (var index in picked)
            {
                claimed[index] = true;
            }

            return VBIB.Concatenate(matched);
        }

        private int Find(uint elementCount, bool[]? pending)
        {
            for (var i = 0; i < toolsBuffers.Count; i++)
            {
                if (claimed[i] || (pending != null && pending[i]))
                {
                    continue;
                }

                if (toolsBuffers[i].ElementCount == elementCount)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
