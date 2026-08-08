using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision Vector3 animation data.
    /// </summary>
    public class CCompressedFullVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads full-precision Vector3 data directly from the data buffer.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;
            var vectorData = MemoryMarshal.Cast<byte, Vector3>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(remap.Dest, ChannelAttribute, vectorData[offset + remap.Source]);
            }
        }
    }
}
