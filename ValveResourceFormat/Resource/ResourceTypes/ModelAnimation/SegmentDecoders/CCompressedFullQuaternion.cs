using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullQuaternion : AnimationSegmentDecoder
    {
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var quaternionData = MemoryMarshal.Cast<byte, Quaternion>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, quaternionData[offset + WantedElements[i]]);
            }
        }
    }
}
