using System;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFloat : AnimationSegmentDecoder
    {
        public float[] Data { get; }

        public CCompressedStaticFloat(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            Data = new float[elements.Length];
            // Static data has only one frame of data, so prefetch all data to avoid unnecessary GC
            for (var i = 0; i < elements.Length; i++)
            {
                Data[i] = BitConverter.ToSingle(data.Slice(i * 4));
            }
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
