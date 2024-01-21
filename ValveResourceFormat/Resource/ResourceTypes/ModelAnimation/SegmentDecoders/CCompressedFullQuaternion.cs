using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullQuaternion : AnimationSegmentDecoder
    {
        private readonly Quaternion[] Data;

        public CCompressedFullQuaternion(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            const int elementSize = 4 * 4;
            var stride = elementCount * elementSize;
            Data = Enumerable.Range(0, data.Count / stride)
                .SelectMany(i => wantedElements.Select(j =>
                {
                    var offset = i * stride + j * elementSize;
                    return new Quaternion(
                        BitConverter.ToSingle(data.Slice(offset + (0 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (1 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (2 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (3 * 4)))
                    );
                }).ToArray())
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * RemapTable.Length;
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Data[offset + i]);
            }
        }
    }
}
