using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision float animation data.
    /// </summary>
    public class CCompressedFullFloat : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads full-precision float data directly from the data buffer.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;
            var floatData = MemoryMarshal.Cast<byte, float>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(remap.Dest, ChannelAttribute, floatData[offset + remap.Source]);
            }
        }
    }
}
