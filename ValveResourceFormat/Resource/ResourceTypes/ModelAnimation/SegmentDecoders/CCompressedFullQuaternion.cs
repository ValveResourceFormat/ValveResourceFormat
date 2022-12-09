using System;
using System.Numerics;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullQuaternion : AnimationSegmentDecoder
    {
        public Quaternion[] Data { get; }

        public CCompressedFullQuaternion(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = Enumerable.Range(0, data.Count / (4 * 4))
                .Select(i =>
                {
                    var offset = i * (4 * 4);
                    return new Quaternion(
                        BitConverter.ToSingle(data.Slice(offset + (0 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (1 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (2 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (3 * 4)))
                    );
                })
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
