namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static compressed quaternion data that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < RemapTable.Length; i++)
            {
                var compressedQuaternionBytes = Data.Slice(
                    WantedElements[i] * SegmentHelpers.CompressedQuaternionSize,
                    SegmentHelpers.CompressedQuaternionSize
                );

                var quaternion = SegmentHelpers.ReadQuaternion(compressedQuaternionBytes);

                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, quaternion);
            }
        }
    }
}
