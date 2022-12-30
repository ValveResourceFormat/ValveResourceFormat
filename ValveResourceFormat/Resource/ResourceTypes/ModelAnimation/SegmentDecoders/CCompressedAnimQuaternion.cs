using System;

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

            Data = new byte[data.Count];

            var pos = 0;
            for (var i = 0; i < data.Count / stride; i++)
            {
                foreach (var j in wantedElements)
                {
                    data.Slice(i * stride + j * elementSize, elementSize).CopyTo(Data, pos);
                    pos += elementSize;
                }
            }
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
