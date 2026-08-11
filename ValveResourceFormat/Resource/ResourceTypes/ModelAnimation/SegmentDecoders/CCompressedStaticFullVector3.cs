using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static full-precision Vector3 data that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static Vector3 values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var vectorData = MemoryMarshal.Cast<byte, Vector3>(Data.Span);

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(remap.Dest, ChannelAttribute, vectorData[remap.Source]);
            }
        }
    }
}
