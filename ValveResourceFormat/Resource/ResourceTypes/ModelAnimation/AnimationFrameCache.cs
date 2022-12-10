using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationFrameCache
    {
        private (int FrameIndex, Frame Frame) PreviousFrame { get; set; } = (-1, new Frame());
        private (int FrameIndex, Frame Frame) NextFrame { get; set; } = (-1, new Frame());
        private Frame InterpolatedFrame { get; } = new Frame();

        /// <summary>
        /// Get the animation frame at a time.
        /// </summary>
        /// <param name="time">The time to get the frame for.</param>
        public Frame GetFrame(Animation anim, float time)
        {
            // Calculate the index of the current frame
            var frameIndex = (int)(time * anim.Fps) % anim.FrameCount;
            var t = ((time * anim.Fps) - frameIndex) % 1;

            // Get current and next frame
            var frame1 = GetFrame(anim, frameIndex);
            var frame2 = GetFrame(anim, (frameIndex + 1) % anim.FrameCount);

            // Interpolate bone positions, angles and scale
            foreach (var bonePair in frame1.Bones)
            {
                var frame1Bone = frame1.Bones[bonePair.Key];
                var frame2Bone = frame2.Bones[bonePair.Key];
                var position = Vector3.Lerp(frame1Bone.Position, frame2Bone.Position, t);
                var angle = Quaternion.Slerp(frame1Bone.Angle, frame2Bone.Angle, t);
                var scale = frame1Bone.Scale + (frame2Bone.Scale - frame1Bone.Scale) * t;

                if (InterpolatedFrame.Bones.TryGetValue(bonePair.Key, out var interpolatedBone))
                {
                    interpolatedBone.Position = position;
                    interpolatedBone.Angle = angle;
                    interpolatedBone.Scale = scale;
                }
                else
                {
                    InterpolatedFrame.Bones.Add(bonePair.Key, new FrameBone(position, angle, scale));
                }
            }

            return InterpolatedFrame;
        }

        /// <summary>
        /// Clears interpolated frame bones and frame cache.
        /// Should be used on animation change.
        /// </summary>
        public void Clear()
        {
            PreviousFrame.Frame.Bones.Clear();
            PreviousFrame = (-1, PreviousFrame.Frame);

            NextFrame.Frame.Bones.Clear();
            NextFrame = (-1, NextFrame.Frame);

            InterpolatedFrame.Bones.Clear();
        }

        private Frame GetFrame(Animation anim, int frameIndex)
        {
            // Try to lookup cached (precomputed) frame - happens when GUI Autoplay runs faster than animation FPS
            if (frameIndex == PreviousFrame.FrameIndex)
            {
                return PreviousFrame.Frame;
            }
            if (frameIndex == NextFrame.FrameIndex)
            {
                return NextFrame.Frame;
            }

            // Only two frames are cached at a time to minimize memory usage, especially with Autoplay enabled
            Frame frame;
            if (frameIndex > PreviousFrame.FrameIndex)
            {
                frame = PreviousFrame.Frame;
                PreviousFrame = NextFrame;
                NextFrame = (frameIndex, frame);
            }
            else
            {
                frame = NextFrame.Frame;
                NextFrame = PreviousFrame;
                PreviousFrame = (frameIndex, frame);
            }
            // We make an assumption that frames contain identical bone sets in GetFrame,
            // so we don't clear bones here and reuse their objects

            anim.DecodeFrame(frameIndex, frame);

            return frame;
        }
    }
}
