using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision float animation data.
    /// </summary>
    public class CCompressedFullFloat : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var floatData = MemoryMarshal.Cast<byte, float>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, floatData[offset + WantedElements[i]]);
            }
        }
    }
}
