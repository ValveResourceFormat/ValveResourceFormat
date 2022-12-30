using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimVector3 : AnimationSegmentDecoder
    {
        public Half[] Data { get; }

        public CCompressedAnimVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            const int elementSize = 2;
            var stride = elementCount * elementSize;
            Data = Enumerable.Range(0, data.Count / stride)
                .SelectMany(i => wantedElements.Select(j =>
                {
                    return BitConverter.ToHalf(data.Slice(i * stride + j * elementSize));
                }).ToArray())
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * RemapTable.Length * 3;

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    new Vector3(
                        (float)Data[offset + i * 3 + 0],
                        (float)Data[offset + i * 3 + 1],
                        (float)Data[offset + i * 3 + 2]
                    )
                );
            }
        }
    }
}
