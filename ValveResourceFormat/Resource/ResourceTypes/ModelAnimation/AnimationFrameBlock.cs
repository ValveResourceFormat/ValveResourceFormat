using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a block of frames in an animation with associated segment data.
    /// </summary>
    public class AnimationFrameBlock
    {
        /// <summary>
        /// Gets the starting frame index of this block.
        /// </summary>
        public int StartFrame { get; }

        /// <summary>
        /// Gets the ending frame index of this block.
        /// </summary>
        public int EndFrame { get; }

        /// <summary>
        /// Gets the array of segment indices for this frame block.
        /// </summary>
        public long[] SegmentIndexArray { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationFrameBlock"/> class.
        /// </summary>
        public AnimationFrameBlock(KVObject frameBlock)
        {
            StartFrame = frameBlock.GetInt32Property("m_nStartFrame");
            EndFrame = frameBlock.GetInt32Property("m_nEndFrame");
            SegmentIndexArray = frameBlock.GetIntegerArray("m_segmentIndexArray");
        }
    }
}
