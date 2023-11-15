using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder<Quaternion>
    {
        private readonly Quaternion[] Data;

        public CCompressedStaticQuaternion(AnimationSegmentDecoderContext context) : base(context)
        {
            Data = Context.WantedElements.Select(i =>
            {
                return SegmentHelpers.ReadQuaternion(Context.Data.Slice(i * 6));
            }).ToArray();
        }

        public override Quaternion Read(int frameIndex, int i)
        {
            return Data[i];
        }
    }
}
