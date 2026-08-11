using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static Vector3 data using half-precision floats that don't change per frame.
    /// </summary>
    public class CCompressedStaticVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static half-precision Vector3 values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(remap.Dest, ChannelAttribute, halfVectorData[remap.Source]);
            }
        }
    }
}
