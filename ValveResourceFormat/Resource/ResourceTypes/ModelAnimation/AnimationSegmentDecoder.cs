#nullable disable

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public abstract class AnimationSegmentDecoder
    {
        protected ArraySegment<byte> Data { get; private set; }
        protected int[] WantedElements { get; private set; }
        protected int ElementCount { get; private set; }

        public int[] RemapTable { get; private set; }
        public AnimationChannelAttribute ChannelAttribute { get; private set; }

        public void Initialize(ArraySegment<byte> data, int[] wantedElements, int[] remapTable, AnimationChannelAttribute channelAttribute,
            int elementCount = 1)
        {
            Data = data;
            WantedElements = wantedElements;
            ElementCount = elementCount;

            RemapTable = remapTable;
            ChannelAttribute = channelAttribute;

            if (RemapTable.Length != WantedElements.Length)
            {
                throw new ArgumentException("RemapTable and WantedElements must be the same length");
            }
        }

        /// <summary>
        /// Reads and decodes animation data for a specific frame.
        /// </summary>
        /// <param name="frameIndex">The index of the frame to read.</param>
        /// <param name="outFrame">The frame object to populate with decoded data.</param>
        public abstract void Read(int frameIndex, Frame outFrame);
    }
}
