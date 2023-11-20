using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullVector3 : AnimationSegmentDecoder<Vector3>
    {
        private readonly Vector3[] Data;

        public CCompressedFullVector3(AnimationSegmentDecoderContext context) : base(context)
        {
            const int elementSize = 3 * 4;
            var stride = Context.Elements.Length * elementSize;
            var elements = Context.Data.Count / stride;

            Data = new Vector3[Context.Elements.Length * elements];

            var pos = 0;
            for (var i = 0; i < elements; i++)
            {
                foreach (var j in Context.WantedElements)
                {
                    var offset = i * stride + j * elementSize;
                    Data[pos++] = new Vector3(
                        BitConverter.ToSingle(Context.Data.Slice(offset + (0 * 4), 4)),
                        BitConverter.ToSingle(Context.Data.Slice(offset + (1 * 4), 4)),
                        BitConverter.ToSingle(Context.Data.Slice(offset + (2 * 4), 4))
                    );
                }
            }
        }

        public override Vector3 Read(int frameIndex, int i)
        {
            var offset = frameIndex * Context.RemapTable.Length;
            return Data[offset + i];
        }
    }
}
