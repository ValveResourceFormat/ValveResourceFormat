using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationFrameCache
    {
        private Frame PrevFrame;
        private Frame NextFrame;
        private readonly Frame InterpolatedFrame;
        public Skeleton Skeleton { get; }

        public AnimationFrameCache(Skeleton skeleton, FlexController[] flexControllers)
        {
            PrevFrame = new Frame(skeleton, flexControllers);
            NextFrame = new Frame(skeleton, flexControllers);
            InterpolatedFrame = new Frame(skeleton, flexControllers);
            Skeleton = skeleton;
            Clear();
        }

        /// <summary>
        /// Clears interpolated frame bones and frame cache.
        /// Should be used on animation change.
        /// </summary>
        public void Clear()
        {
            PrevFrame.Clear(Skeleton);
            NextFrame.Clear(Skeleton);
        }

        /// <summary>
        /// Get the animation frame at a time.
        /// </summary>
        /// <param name="time">The time to get the frame for.</param>
        public Frame GetInterpolatedFrame(Animation anim, float time)
        {
            if (anim.FrameCount <= 1)
            {
                return GetFrame(anim, 0);
            }

            // Calculate the index of the current frame
            var frameIndex = (int)(time * anim.Fps) % (anim.FrameCount - 1);
            var nextFrameIndex = (frameIndex + 1) % anim.FrameCount;
            var t = ((time * anim.Fps) - frameIndex) % 1;

            // Get current and next frame
            var frame1 = GetFrame(anim, frameIndex);
            var frame2 = GetFrame(anim, nextFrameIndex);

            // Make sure second GetFrame call didn't return incorrect instance
            Debug.Assert(frame1.FrameIndex == frameIndex);
            Debug.Assert(frame2.FrameIndex == nextFrameIndex);

            // Interpolate bone positions, angles and scale
            for (var i = 0; i < frame1.Bones.Length; i++)
            {
                var frame1Bone = frame1.Bones[i];
                var frame2Bone = frame2.Bones[i];
                InterpolatedFrame.Bones[i].Position = Vector3.Lerp(frame1Bone.Position, frame2Bone.Position, t);
                InterpolatedFrame.Bones[i].Angle = Quaternion.Slerp(frame1Bone.Angle, frame2Bone.Angle, t);
                InterpolatedFrame.Bones[i].Scale = float.Lerp(frame1Bone.Scale, frame2Bone.Scale, t);
            }

            for (var i = 0; i < frame1.Datas.Length; i++)
            {
                var frame1Data = frame1.Datas[i];
                var frame2Data = frame2.Datas[i];

                InterpolatedFrame.Datas[i] = float.Lerp(frame1Data, frame2Data, t);
            }

            if (anim.HasMovementData())
            {
                InterpolatedFrame.Movement = new(
                    Vector3.Lerp(frame1.Movement.Position, frame2.Movement.Position, t),
                    float.Lerp(frame1.Movement.Angle, frame2.Movement.Angle, t)
                );
            }

            return InterpolatedFrame;
        }

        /// <summary>
        /// Get the animation frame at a given index.
        /// </summary>
        public Frame GetFrame(Animation anim, int frameIndex)
        {
            // Try to lookup cached (precomputed) frame - happens when GUI Autoplay runs faster than animation FPS
            if (frameIndex == PrevFrame.FrameIndex)
            {
                return PrevFrame;
            }

            var frame = NextFrame;
            NextFrame = PrevFrame;
            PrevFrame = frame;

            // Only two frames are cached at a time to minimize memory usage, especially with Autoplay enabled
            if (frameIndex == frame.FrameIndex)
            {
                return frame;
            }

            // We make an assumption that frames within one animation
            // contain identical bone sets, so we don't clear frame here
            frame.FrameIndex = frameIndex;
            anim.DecodeFrame(frame);

            frame.Movement = anim.GetMovementOffsetData(frameIndex);
            return frame;
        }
    }
}
