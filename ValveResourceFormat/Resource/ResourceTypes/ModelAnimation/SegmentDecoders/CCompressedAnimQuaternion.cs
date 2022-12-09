using System;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimQuaternion : AnimationSegmentDecoder
    {
        public ArraySegment<byte> Data { get; }

        public CCompressedAnimQuaternion(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = data;
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = 6 * Elements.Length * frameIndex;

            for (var element = 0; element < Elements.Length; element++)
            {
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    SegmentHelpers.ReadQuaternion(Data.Slice(offset))
                );
                offset += 6;
            }
        }
    }
}
