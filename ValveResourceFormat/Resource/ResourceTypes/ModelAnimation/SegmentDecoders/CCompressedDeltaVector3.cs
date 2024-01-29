namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedDeltaVector3 : AnimationSegmentDecoder
    {
        private readonly Vector3[] BaseFrame;
        private readonly Half[] DeltaData;

        public CCompressedDeltaVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            const int baseElementSize = 4;
            const int deltaElementSize = 2;

            BaseFrame = new Vector3[wantedElements.Length];

            var pos = 0;
            foreach (var i in wantedElements)
            {
                var offset = i * 3 * baseElementSize;
                BaseFrame[pos++] = new Vector3(
                    BitConverter.ToSingle(data.Slice(offset + (0 * baseElementSize), baseElementSize)),
                    BitConverter.ToSingle(data.Slice(offset + (1 * baseElementSize), baseElementSize)),
                    BitConverter.ToSingle(data.Slice(offset + (2 * baseElementSize), baseElementSize))
                );
            }

            var deltaData = data.Slice(elementCount * 3 * baseElementSize);
            var stride = elementCount * deltaElementSize;
            var elements = deltaData.Count / stride;
            var frames = elements / 3;

            DeltaData = new Half[remapTable.Length * elements];

            pos = 0;
            for (var i = 0; i < frames; i++)
            {
                foreach (var j in wantedElements)
                {
                    DeltaData[pos++] = BitConverter.ToHalf(deltaData.Slice(i * stride * 3 + j * deltaElementSize * 3 + deltaElementSize * 0, deltaElementSize));
                    DeltaData[pos++] = BitConverter.ToHalf(deltaData.Slice(i * stride * 3 + j * deltaElementSize * 3 + deltaElementSize * 1, deltaElementSize));
                    DeltaData[pos++] = BitConverter.ToHalf(deltaData.Slice(i * stride * 3 + j * deltaElementSize * 3 + deltaElementSize * 2, deltaElementSize));
                }
            }
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * RemapTable.Length * 3;

            for (var i = 0; i < RemapTable.Length; i++)
            {
                var baseFrame = BaseFrame[i];
                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    baseFrame + new Vector3(
                        (float)DeltaData[offset + i * 3 + 0],
                        (float)DeltaData[offset + i * 3 + 1],
                        (float)DeltaData[offset + i * 3 + 2]
                    )
                );
            }
        }
    }
}
