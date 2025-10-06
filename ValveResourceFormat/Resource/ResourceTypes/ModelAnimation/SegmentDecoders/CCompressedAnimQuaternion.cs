namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedAnimQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;

            for (var i = 0; i < RemapTable.Length; i++)
            {
                var elementIndex = WantedElements[i];

                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    SegmentHelpers.ReadQuaternion(Data.AsSpan(
                        (offset + elementIndex) * SegmentHelpers.CompressedQuaternionSize,
                        SegmentHelpers.CompressedQuaternionSize
                    ))
                );
            }
        }
    }
}
