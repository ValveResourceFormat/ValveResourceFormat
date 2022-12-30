using System;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimQuaternion : AnimationSegmentDecoder
    {
        public byte[] Data { get; }

        public CCompressedAnimQuaternion(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            const int elementSize = 6;
            var stride = elementCount * elementSize;
            Data = Enumerable.Range(0, data.Count / stride)
                .SelectMany(i => wantedElements.SelectMany(j =>
                {
                    return data.Slice(i * stride + j * elementSize, elementSize);
                }).ToArray())
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            const int elementSize = 6;
            var offset = frameIndex * RemapTable.Length * elementSize;
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    SegmentHelpers.ReadQuaternion(new ReadOnlySpan<byte>(
                        Data,
                        offset + i * elementSize,
                        elementSize
                    ))
                );
            }
        }
    }
}
