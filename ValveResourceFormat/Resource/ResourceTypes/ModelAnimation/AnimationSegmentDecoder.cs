using System;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public abstract class AnimationSegmentDecoder
    {
        public int[] Elements { get; }
        public AnimationDataChannel LocalChannel { get; }

        protected AnimationSegmentDecoder(int[] elements, AnimationDataChannel localChannel)
        {
            Elements = elements;
            LocalChannel = localChannel;
        }

        public abstract void Read(int frameIndex, Frame outFrame);
    }
}
