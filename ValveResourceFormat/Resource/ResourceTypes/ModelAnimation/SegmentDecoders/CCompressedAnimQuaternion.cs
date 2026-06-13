namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes compressed quaternion animation data.
    /// </summary>
    public class CCompressedAnimQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads compressed quaternion data and decompresses it into the output frame.
        /// </remarks>
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
