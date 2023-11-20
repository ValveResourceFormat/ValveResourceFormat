using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimQuaternion : AnimationSegmentDecoder<Quaternion>
    {
        private readonly byte[] Data;

        public CCompressedAnimQuaternion(AnimationSegmentDecoderContext context) : base(context)
        {
            const int elementSize = 6;
            var stride = Context.Elements.Length * elementSize;
            var elements = Context.Data.Count / stride;

            Data = new byte[Context.Elements.Length * elementSize * elements];

            var pos = 0;
            for (var i = 0; i < elements; i++)
            {
                foreach (var j in Context.WantedElements)
                {
                    Context.Data.Slice(i * stride + j * elementSize, elementSize).CopyTo(Data, pos);
                    pos += elementSize;
                }
            }
        }

        public override Quaternion Read(int frameIndex, int i)
        {
            const int elementSize = 6;
            var offset = frameIndex * Context.RemapTable.Length * elementSize;

            return SegmentHelpers.ReadQuaternion(new ReadOnlySpan<byte>(
                    Data,
                    offset + i * elementSize,
                    elementSize
                ));
        }
    }
}
