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
        public override void Read(int frameIndex, Frame outFrame)
        {
            var vectorData = MemoryMarshal.Cast<byte, Vector3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, vectorData[WantedElements[i]]);
            }
        }
    }
}
