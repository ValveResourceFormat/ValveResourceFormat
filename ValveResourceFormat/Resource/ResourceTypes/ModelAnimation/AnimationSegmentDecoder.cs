#nullable disable

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Base class for animation segment decoders that read compressed animation data.
    /// </summary>
    public abstract class AnimationSegmentDecoder
    {
        /// <summary>
        /// Gets the raw animation data segment.
        /// </summary>
        protected ArraySegment<byte> Data { get; private set; }

        /// <summary>
        /// Gets the array of element indices to decode.
        /// </summary>
        protected int[] WantedElements { get; private set; }

        /// <summary>
        /// Gets the total number of elements in the segment.
        /// </summary>
        protected int ElementCount { get; private set; }

        /// <summary>
        /// Gets the remap table for mapping elements to bones or flex controllers.
        /// </summary>
        public int[] RemapTable { get; private set; }

        /// <summary>
        /// Gets the channel attribute type.
        /// </summary>
        public AnimationChannelAttribute ChannelAttribute { get; private set; }

        /// <summary>
        /// Initializes the decoder with data and mapping information.
        /// </summary>
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
