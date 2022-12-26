using System;
using System.Numerics;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedDeltaVector3 : AnimationSegmentDecoder
    {
        public float[] BaseFrame { get; }
        public Half[] DeltaData { get; }

        public CCompressedDeltaVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            BaseFrame = wantedElements.SelectMany(i =>
            {
                var offset = i * 3 * 4;
                return new float[3]
                {
                    BitConverter.ToSingle(data.Slice(offset + (0 * 4))),
                    BitConverter.ToSingle(data.Slice(offset + (1 * 4))),
                    BitConverter.ToSingle(data.Slice(offset + (2 * 4)))
                };
            }).ToArray();

            var deltaData = data.Slice(elementCount * 3 * 4);
            const int elementSize = 2;
            var stride = elementCount * elementSize;
            DeltaData = Enumerable.Range(0, deltaData.Count / stride)
                .SelectMany(i => wantedElements.Select(j =>
                {
                    return BitConverter.ToHalf(deltaData.Slice(i * stride + j * elementSize));
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
                        BaseFrame[i * 3 + 0] + (float)DeltaData[offset + i * 3 + 0],
                        BaseFrame[i * 3 + 1] + (float)DeltaData[offset + i * 3 + 1],
                        BaseFrame[i * 3 + 2] + (float)DeltaData[offset + i * 3 + 2]
                    )
                );
            }
        }
    }
}
