using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullQuaternion : AnimationSegmentDecoder<Quaternion>
    {
        private readonly Quaternion[] Data;

        public CCompressedFullQuaternion(AnimationSegmentDecoderContext context) : base(context)
        {
            const int elementSize = 4 * 4;
            var stride = Context.Elements.Length * elementSize;
            Data = Enumerable.Range(0, Context.Data.Count / stride)
                .SelectMany(i => Context.WantedElements.Select(j =>
                {
                    var offset = i * stride + j * elementSize;
                    return new Quaternion(
                        BitConverter.ToSingle(Context.Data.Slice(offset + (0 * 4))),
                        BitConverter.ToSingle(Context.Data.Slice(offset + (1 * 4))),
                        BitConverter.ToSingle(Context.Data.Slice(offset + (2 * 4))),
                        BitConverter.ToSingle(Context.Data.Slice(offset + (3 * 4)))
                    );
                }).ToArray())
                .ToArray();
        }

        public override Quaternion Read(int frameIndex, int i)
        {
            var offset = frameIndex * Context.RemapTable.Length;
            return Data[offset + i];
        }
    }
}
