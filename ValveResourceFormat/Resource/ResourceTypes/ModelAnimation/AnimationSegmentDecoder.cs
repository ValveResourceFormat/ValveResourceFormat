namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Maps one element of an animation segment onto the bone or flex controller it drives.
    /// </summary>
    public readonly record struct ElementRemap(int Source, int Dest);

    /// <summary>
    /// Base class for animation segment decoders that read compressed animation data.
    /// </summary>
    /// <remarks>
    /// Decoders hold only the segment's own data and are independent of any target skeleton. Which bones
    /// or flex controllers a segment writes to arrives per call, from an <see cref="AnimationBinding"/>.
    /// </remarks>
    public abstract class AnimationSegmentDecoder
    {
        /// <summary>
        /// Gets the raw animation data segment.
        /// </summary>
        /// <remarks>References the kv3 blob, not a copy.</remarks>
        protected ReadOnlyMemory<byte> Data { get; private set; }

        /// <summary>
        /// Gets the total number of elements in the segment.
        /// </summary>
        protected int ElementCount { get; private set; }

        /// <summary>
        /// Gets the channel attribute type.
        /// </summary>
        public AnimationChannelAttribute ChannelAttribute { get; private set; }

        /// <summary>
        /// Initializes the decoder with its segment data.
        /// </summary>
        public void Initialize(ReadOnlyMemory<byte> data, AnimationChannelAttribute channelAttribute, int elementCount = 1)
        {
            Data = data;
            ElementCount = elementCount;
            ChannelAttribute = channelAttribute;
        }

        /// <summary>
        /// Reads and decodes animation data for a specific frame.
        /// </summary>
        /// <param name="frameIndex">The index of the frame to read.</param>
        /// <param name="outFrame">The frame object to populate with decoded data.</param>
        /// <param name="remaps">The elements to write, and the bone or flex controller each one drives.</param>
        public abstract void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps);
    }
}
