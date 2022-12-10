using System;
using System.Numerics;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder
    {
        public Vector3[] Data { get; }

        public CCompressedStaticFullVector3(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            // Static data has only one frame of data, so prefetch all data to avoid unnecessary GC
            Data = Enumerable.Range(0, data.Count / (3 * 4))
                .Select(i =>
                {
                    var offset = i * 3 * 4;
                    return new Vector3(
                        BitConverter.ToSingle(data.Slice(offset + (0 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (1 * 4))),
                        BitConverter.ToSingle(data.Slice(offset + (2 * 4)))
                    );
                })
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var element = 0; element < Elements.Length; element++)
            {
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    Data[element]
                );
            }
        }
    }
}
