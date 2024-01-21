using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder
    {
        private readonly Quaternion[] Data;

        public CCompressedStaticQuaternion(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            Data = wantedElements.Select(i =>
            {
                return SegmentHelpers.ReadQuaternion(data.Slice(i * 6));
            }).ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Data[i]);
            }
        }
    }
}
