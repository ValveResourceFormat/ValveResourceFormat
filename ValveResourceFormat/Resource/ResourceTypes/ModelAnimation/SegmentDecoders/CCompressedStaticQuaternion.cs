namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static compressed quaternion data that doesn't change per frame.
    /// </summary>
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads static compressed quaternion values that remain constant across all frames.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var data = Data.Span;

            foreach (var remap in remaps)
            {
                var compressedQuaternionBytes = data.Slice(
                    remap.Source * SegmentHelpers.CompressedQuaternionSize,
                    SegmentHelpers.CompressedQuaternionSize
                );

                var quaternion = SegmentHelpers.ReadQuaternion(compressedQuaternionBytes);

                outFrame.SetAttribute(remap.Dest, ChannelAttribute, quaternion);
            }
        }
    }
}
