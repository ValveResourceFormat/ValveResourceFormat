using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticVector3 : AnimationSegmentDecoder
    {
        public override void Read(int frameIndex, Frame outFrame)
        {
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, halfVectorData[WantedElements[i]]);
            }
        }
    }
}
