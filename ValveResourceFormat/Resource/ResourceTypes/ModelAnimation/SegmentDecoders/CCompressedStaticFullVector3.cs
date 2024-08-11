using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder
    {
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
