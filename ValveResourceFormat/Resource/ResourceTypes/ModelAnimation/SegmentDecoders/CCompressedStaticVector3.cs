using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticVector3 : AnimationSegmentDecoder<Vector3>
    {
        private readonly Vector3[] Data;

        public CCompressedStaticVector3(AnimationSegmentDecoderContext context) : base(context)
        {
            Data = Context.WantedElements.Select(i =>
            {
                var offset = i * (3 * 2);
                return new Vector3(
                    (float)BitConverter.ToHalf(Context.Data.Slice(offset + (0 * 2))),
                    (float)BitConverter.ToHalf(Context.Data.Slice(offset + (1 * 2))),
                    (float)BitConverter.ToHalf(Context.Data.Slice(offset + (2 * 2)))
                );
            }).ToArray();
        }

        public override Vector3 Read(int frameIndex, int i)
        {
            return Data[i];
        }
    }
}
