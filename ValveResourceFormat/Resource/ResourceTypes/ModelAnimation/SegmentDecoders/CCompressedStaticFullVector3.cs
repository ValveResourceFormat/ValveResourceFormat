using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder
    {
        private readonly Vector3[] Data;

        public CCompressedStaticFullVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            Data = wantedElements.Select(i =>
            {
                var offset = i * (3 * 4);
                return new Vector3(
                    BitConverter.ToSingle(data.Slice(offset + (0 * 4))),
                    BitConverter.ToSingle(data.Slice(offset + (1 * 4))),
                    BitConverter.ToSingle(data.Slice(offset + (2 * 4)))
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
