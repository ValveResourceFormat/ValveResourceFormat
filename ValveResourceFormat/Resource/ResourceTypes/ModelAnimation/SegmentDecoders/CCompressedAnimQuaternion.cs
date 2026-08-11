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
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;
            var data = Data.Span;

            foreach (var remap in remaps)
            {
                outFrame.SetAttribute(
                    remap.Dest,
                    ChannelAttribute,
                    SegmentHelpers.ReadQuaternion(data.Slice(
                        (offset + remap.Source) * SegmentHelpers.CompressedQuaternionSize,
                        SegmentHelpers.CompressedQuaternionSize
                    ))
                );
            }
        }
    }
}
