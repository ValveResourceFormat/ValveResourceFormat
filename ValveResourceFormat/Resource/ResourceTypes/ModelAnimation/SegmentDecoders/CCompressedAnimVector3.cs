using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes compressed Vector3 animation data using half-precision floats.
    /// </summary>
    public class CCompressedAnimVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads half-precision Vector3 data and converts it to full precision for the output frame.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(
                    remap.Dest,
                    ChannelAttribute,
                    halfVectorData[offset + remap.Source]
                );
            }
        }
    }
}
