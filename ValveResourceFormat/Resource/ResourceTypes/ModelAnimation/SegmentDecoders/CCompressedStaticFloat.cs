using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFloat : AnimationSegmentDecoder
    {
        private readonly float[] Data;

        public CCompressedStaticFloat(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            Data = wantedElements.Select(i =>
            {
                return BitConverter.ToSingle(data.Slice(i * 4));
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
