namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static bool data that doesn't change per frame, stored as one byte per element.
    /// </summary>
    internal class CCompressedStaticBool : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Data[WantedElements[i]] != 0 ? 1f : 0f);
            }
        }
    }
}
