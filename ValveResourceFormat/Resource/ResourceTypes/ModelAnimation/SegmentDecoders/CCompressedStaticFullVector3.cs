using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticFullVector3 : AnimationSegmentDecoder<Vector3>
    {
        private readonly Vector3[] Data;

        public CCompressedStaticFullVector3(AnimationSegmentDecoderContext context) : base(context)
        {
            Data = Context.WantedElements.Select(i =>
            {
                var offset = i * (3 * 4);
                return new Vector3(
                    BitConverter.ToSingle(Context.Data.Slice(offset + (0 * 4))),
                    BitConverter.ToSingle(Context.Data.Slice(offset + (1 * 4))),
                    BitConverter.ToSingle(Context.Data.Slice(offset + (2 * 4)))
                );
            }).ToArray();
        }

        public override Vector3 Read(int frameIndex, int i)
        {
            return Data[i];
        }
    }
}
