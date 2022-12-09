using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder
    {
        public Quaternion[] Data { get; }

        public CCompressedStaticQuaternion(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = new Quaternion[elements.Length];
            // Static data has only one frame of data, so prefetch all data to avoid unnecessary GC
            for (var i = 0; i < elements.Length; i++)
            {
                Data[i] = SegmentHelpers.ReadQuaternion(data.Slice(i * 6));
            }
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var element = 0; element < Elements.Length; element++)
            {
                // Get the bone we are reading for
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    Data[element]
                );
            }
        }
    }
}
