using System;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public interface IAnimationSegmentDecoder
    {
        public AnimationSegmentDecoderContext Context { get; }
        public void Read(int frameIndex, Frame outFrame);
    }
    public abstract class AnimationSegmentDecoder<T> : IAnimationSegmentDecoder
    {
        public AnimationSegmentDecoderContext Context { get; }

        protected AnimationSegmentDecoder(AnimationSegmentDecoderContext context)
        {
            Context = context;
        }

        public abstract T Read(int frameIndex, int i);
        public void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < Context.RemapTable.Length; i++)
            {
                outFrame.SetAttribute(Context.RemapTable[i], Context.Channel.Attribute, Read(frameIndex, i));
            }
        }
    }
}
