using System;
using System.Numerics;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedDeltaVector3 : AnimationSegmentDecoder
    {
        public float[] BaseFrame { get; }
        public Half[] DeltaData { get; }

        public CCompressedDeltaVector3(ArraySegment<byte> data, int[] elements, AnimationDataChannel localChannel) : base(elements, localChannel)
        {
            // Prefetch the base frame to avoid using two readers at a time
            BaseFrame = Enumerable.Range(0, elements.Length * 3)
                .Select(i => BitConverter.ToSingle(data.Slice(i * 4)))
                .ToArray();

            var deltaData = data.Slice(elements.Length * 3 * 4);
            DeltaData = Enumerable.Range(0, deltaData.Count / 2)
                .Select(i => BitConverter.ToHalf(deltaData.Slice(i * 2)))
                .ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var baseOffset = 0;
            var deltaOffset = 3 * Elements.Length * frameIndex;

            for (var element = 0; element < Elements.Length; element++)
            {
                outFrame.SetAttribute(
                    LocalChannel.BoneNames[Elements[element]],
                    LocalChannel.ChannelAttribute,
                    new Vector3(
                        BaseFrame[baseOffset + 0] + (float)DeltaData[deltaOffset + 0],
                        BaseFrame[baseOffset + 1] + (float)DeltaData[deltaOffset + 1],
                        BaseFrame[baseOffset + 2] + (float)DeltaData[deltaOffset + 2]
                    )
                );
                baseOffset += 3;
                deltaOffset += 3;
            }
        }
    }
}
