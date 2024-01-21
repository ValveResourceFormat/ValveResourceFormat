namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedFullVector3 : AnimationSegmentDecoder
    {
        private readonly Vector3[] Data;

        public CCompressedFullVector3(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            int elementCount, AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            const int elementSize = 3 * 4;
            var stride = elementCount * elementSize;
            var elements = data.Count / stride;

            Data = new Vector3[remapTable.Length * elements];

            var pos = 0;
            for (var i = 0; i < elements; i++)
            {
                foreach (var j in wantedElements)
                {
                    var offset = i * stride + j * elementSize;
                    Data[pos++] = new Vector3(
                        BitConverter.ToSingle(data.Slice(offset + (0 * 4), 4)),
                        BitConverter.ToSingle(data.Slice(offset + (1 * 4), 4)),
                        BitConverter.ToSingle(data.Slice(offset + (2 * 4), 4))
                    );
                }
            }
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * RemapTable.Length;
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Data[offset + i]);
            }
        }
    }
}
