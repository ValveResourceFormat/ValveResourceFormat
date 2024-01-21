using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticVector3 : AnimationSegmentDecoder
    {
        private readonly Vector3[] Data;

        public CCompressedStaticVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            Data = wantedElements.Select(i =>
            {
                var offset = i * (3 * 2);
                return new Vector3(
                    (float)BitConverter.ToHalf(data.Slice(offset + (0 * 2))),
                    (float)BitConverter.ToHalf(data.Slice(offset + (1 * 2))),
                    (float)BitConverter.ToHalf(data.Slice(offset + (2 * 2)))
                );
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
