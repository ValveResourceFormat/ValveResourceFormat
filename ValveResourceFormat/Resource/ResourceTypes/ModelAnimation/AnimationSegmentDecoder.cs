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
            if (Context.Channel.Attribute == AnimationChannelAttribute.Data)
            {
                if (typeof(T) != typeof(float))
                {
                    throw new NotImplementedException("Only float types are supported for data channel");
                }

                for (var i = 0; i < Context.Elements.Length; i++)
                {
                    var element = Context.Elements[i];
                    var data = (object)Read(frameIndex, i);
                    outFrame.SetDataAttribute(Context.Channel.IndexToName[element], (float)data);
                }
            }
            else
            {
                for (var i = 0; i < Context.RemapTable.Length; i++)
                {
                    outFrame.SetAttribute(Context.RemapTable[i], Context.Channel.Attribute, Read(frameIndex, i));
                }
            }
        }
    }
}
