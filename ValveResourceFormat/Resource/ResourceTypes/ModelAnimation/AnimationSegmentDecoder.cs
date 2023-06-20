namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public abstract class AnimationSegmentDecoder
    {
        public int[] RemapTable { get; }
        public AnimationChannelAttribute ChannelAttribute { get; }

        protected AnimationSegmentDecoder(int[] remapTable, AnimationChannelAttribute channelAttribute)
        {
            RemapTable = remapTable;
            ChannelAttribute = channelAttribute;
        }

        public abstract void Read(int frameIndex, Frame outFrame);
    }
}
