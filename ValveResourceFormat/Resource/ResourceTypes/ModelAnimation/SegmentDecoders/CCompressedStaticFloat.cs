using System;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFloat : AnimationSegmentDecoder<float>
    {
        private readonly float[] Data;

        public CCompressedStaticFloat(AnimationSegmentDecoderContext context) : base(context)
        {
            Data = context.WantedElements.Select(i =>
            {
                return BitConverter.ToSingle(Context.Data.Slice(i * 4));
            }).ToArray();
        }

        public override float Read(int frameIndex, int i)
        {
            return Data[i];
        }
    }
}
