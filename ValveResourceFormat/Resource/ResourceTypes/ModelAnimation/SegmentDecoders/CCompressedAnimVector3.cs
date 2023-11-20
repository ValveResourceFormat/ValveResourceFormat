using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimVector3 : AnimationSegmentDecoder<Vector3>
    {
        private readonly Half[] Data;

        public CCompressedAnimVector3(AnimationSegmentDecoderContext context) : base(context)
        {
            const int elementSize = 2;
            var stride = Context.Elements.Length * elementSize;
            var elements = context.Data.Count / stride;

            Data = new Half[Context.Elements.Length * elements];

            var pos = 0;
            for (var i = 0; i < elements; i++)
            {
                foreach (var j in context.WantedElements)
                {
                    Data[pos++] = BitConverter.ToHalf(context.Data.Slice(i * stride + j * elementSize, elementSize));
                }
            }
        }

        public override Vector3 Read(int frameIndex, int i)
        {
            var offset = frameIndex * Context.RemapTable.Length * 3;

            return new Vector3(
                    (float)Data[offset + i * 3 + 0],
                    (float)Data[offset + i * 3 + 1],
                    (float)Data[offset + i * 3 + 2]
                );
        }
    }
}
