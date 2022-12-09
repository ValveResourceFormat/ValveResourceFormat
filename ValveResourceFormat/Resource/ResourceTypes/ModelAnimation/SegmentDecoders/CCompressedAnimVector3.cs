using System;
using System.Numerics;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimVector3 : AnimationSegmentDecoder
    {
        public Half[] Data { get; }

        public CCompressedAnimVector3(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = Enumerable.Range(0, data.Count / 2)
                .Select(i => BitConverter.ToHalf(data.Slice(i * 2)))
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = 3 * Elements.Length * frameIndex;

            for (var element = 0; element < Elements.Length; element++)
            {
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    new Vector3(
                        (float)Data[offset + 0],
                        (float)Data[offset + 1],
                        (float)Data[offset + 2]
                    )
                );
                offset += 3;
            }
        }
    }
}
