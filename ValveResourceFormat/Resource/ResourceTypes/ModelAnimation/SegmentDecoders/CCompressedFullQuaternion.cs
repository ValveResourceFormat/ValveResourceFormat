using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision quaternion animation data.
    /// </summary>
    public class CCompressedFullQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads full-precision quaternion data directly from the data buffer.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;
            var quaternionData = MemoryMarshal.Cast<byte, Quaternion>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(remap.Dest, ChannelAttribute, quaternionData[offset + remap.Source]);
            }
        }
    }
}
