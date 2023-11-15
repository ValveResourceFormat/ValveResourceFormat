using System;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullFloat : AnimationSegmentDecoder<float>
    {
        private readonly float[] Data;

        public CCompressedFullFloat(AnimationSegmentDecoderContext context) : base(context)
        {
            const int elementSize = 4;
            var stride = Context.Elements.Length * elementSize;
            Data = Enumerable.Range(0, Context.Data.Count / stride)
                .SelectMany(i => Context.WantedElements.Select(j =>
                {
                    return BitConverter.ToSingle(Context.Data.Slice(i * stride + j * elementSize));
                }).ToArray())
                .ToArray();
        }

        public override float Read(int frameIndex, int i)
        {
            var offset = frameIndex * Context.Elements.Length;
            return Data[offset + i];
        }
    }
}
