using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedDeltaVector3 : AnimationSegmentDecoder<Vector3>
    {
        private readonly Vector3[] BaseFrame;
        private readonly Half[] DeltaData;

        public CCompressedDeltaVector3(AnimationSegmentDecoderContext context) : base(context)
        {
            const int baseElementSize = 4;
            const int deltaElementSize = 2;

            BaseFrame = new Vector3[Context.WantedElements.Length];

            var pos = 0;
            foreach (var i in Context.WantedElements)
            {
                var offset = i * 3 * baseElementSize;
                BaseFrame[pos++] = new Vector3(
                    BitConverter.ToSingle(Context.Data.Slice(offset + (0 * baseElementSize), baseElementSize)),
                    BitConverter.ToSingle(Context.Data.Slice(offset + (1 * baseElementSize), baseElementSize)),
                    BitConverter.ToSingle(Context.Data.Slice(offset + (2 * baseElementSize), baseElementSize))
                );
            }

            var deltaData = Context.Data.Slice(Context.Elements.Length * 3 * baseElementSize);
            var stride = Context.Elements.Length * deltaElementSize;
            var elements = deltaData.Count / stride;

            DeltaData = new Half[Context.Elements.Length * elements];

            pos = 0;
            for (var i = 0; i < elements; i++)
            {
                foreach (var j in Context.WantedElements)
                {
                    DeltaData[pos++] = BitConverter.ToHalf(deltaData.Slice(i * stride + j * deltaElementSize, deltaElementSize));
                }
            }
        }

        public override Vector3 Read(int frameIndex, int i)
        {
            var offset = frameIndex * Context.Elements.Length * 3;
            var baseFrame = BaseFrame[i];
            return baseFrame + new Vector3(
                (float)DeltaData[offset + i * 3 + 0],
                (float)DeltaData[offset + i * 3 + 1],
                (float)DeltaData[offset + i * 3 + 2]
            );
        }
    }
}
