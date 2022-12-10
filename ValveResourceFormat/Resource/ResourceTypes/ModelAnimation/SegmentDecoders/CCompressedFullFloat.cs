using System;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullFloat : AnimationSegmentDecoder
    {
        public float[] Data { get; }

        public CCompressedFullFloat(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = Enumerable.Range(0, data.Count / 4)
                .Select(i => BitConverter.ToSingle(data.Slice(i * 4)))
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = Elements.Length * frameIndex;

            for (var element = 0; element < Elements.Length; element++)
            {
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    Data[offset + element]
                );
            }
        }
    }
}
