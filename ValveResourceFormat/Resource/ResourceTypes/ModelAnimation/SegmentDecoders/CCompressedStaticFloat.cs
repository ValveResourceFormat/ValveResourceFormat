using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static float data that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticFloat : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static float values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var floatData = MemoryMarshal.Cast<byte, float>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, floatData[WantedElements[i]]);
            }
        }
    }
}
