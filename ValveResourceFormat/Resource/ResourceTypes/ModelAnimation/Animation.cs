namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model animation that can be sampled per frame.
    /// </summary>
    public abstract class Animation
    {
        /// <summary>
        /// Gets the name of the animation.
        /// </summary>
        public string Name { get; protected init; } = string.Empty;

        /// <summary>
        /// Gets the frames per second of the animation.
        /// </summary>
        public float Fps { get; protected init; }

        /// <summary>
        /// Gets the total number of frames in the animation.
        /// </summary>
        public int FrameCount { get; protected init; }

        /// <summary>
        /// Gets the duration of the animation in seconds, which is also the period looping playback wraps around.
        /// </summary>
        public virtual float Duration => CycleDuration;

        /// <summary>
        /// Gets the number of frame intervals one playback cycle spans. Looping wraps over the intervals
        /// between samples, so the last frame is where the next cycle starts rather than a sample of its own.
        /// </summary>
        public int CycleFrames => FrameCount - 1;

        /// <summary>
        /// Gets the length of one playback cycle in seconds, the period looping wraps around. Zero when the
        /// animation is a single pose or carries no frame rate.
        /// </summary>
        public float CycleDuration => CycleFrames > 0 && Fps > 0f ? CycleFrames / Fps : 0f;

        /// <summary>
        /// Splits a playback time into the number of whole cycles already played, the frame reached within the
        /// current one, and how far past that frame the time sits. Everything that samples an animation over
        /// time goes through this, so the pose, the root motion and the loop counting cannot drift apart.
        /// </summary>
        /// <remarks>
        /// The frame is relative to the current cycle, and stops one short of <see cref="CycleFrames"/> so the
        /// frame after it is always a valid sample to interpolate towards; the remainder reaches one at the end.
        /// A cycle owns the moment it ends, so a clip parked on its end holds its last frame rather than
        /// reading as frame zero of a cycle it never entered.
        /// </remarks>
        public (int Cycle, int Frame, float Remainder) GetCyclePosition(float time)
        {
            var cycleDuration = CycleDuration;

            if (cycleDuration <= 0f)
            {
                return (0, 0, 0f);
            }

            var cycle = time > 0f ? (int)MathF.Ceiling(time / cycleDuration) - 1 : 0;
            var position = (time - cycle * cycleDuration) * Fps;

            var frame = Math.Min((int)position, CycleFrames - 1);

            return (cycle, frame, position - frame);
        }

        /// <summary>
        /// The playback time of the whole frame nearest <paramref name="time"/>, staying in the cycle being
        /// played. Sampling an exact frame rather than interpolating moves playback by up to half a frame, and
        /// this is what says where to.
        /// </summary>
        public float SnapTimeToFrame(float time)
        {
            if (CycleDuration <= 0f)
            {
                return time;
            }

            var (cycle, frame, remainder) = GetCyclePosition(time);

            return cycle * CycleDuration + (remainder < 0.5f ? frame : frame + 1) / Fps;
        }

        /// <summary>
        /// Gets or sets whether this animation is additive: its frames are per-bone deltas meant to be
        /// composed over a base pose. AG2 clips and AG1 sequences both carry what their own data says; the
        /// owning model then adds the sequences its AG1 graph feeds into additive slots, which is not
        /// knowable from the animation alone.
        /// </summary>
        public bool IsAdditive { get; set; }

        /// <summary>
        /// Gets whether decoding this animation writes any flex controller data.
        /// </summary>
        public virtual bool HasFlexData => false;

        /// <summary>
        /// Gets the resource name of the skeleton this animation is authored on. A model's own
        /// skeleton is named after the vmdl it came from, so animations on it carry that name.
        /// </summary>
        public string TargetSkeletonName { get; protected init; } = string.Empty;

        /// <summary>
        /// The delta a decoded bone of an additive frame contributes, in the one convention everything
        /// composing deltas expects: add the position and the scale, post-multiply the rotation. The two
        /// formats do not decode to that convention on their own, which is what this reconciles.
        /// </summary>
        public abstract FrameBone GetAdditiveDelta(int boneIndex, FrameBone bone);

        /// <summary>
        /// Composes an already-decoded additive frame over the skeleton bind pose, in place.
        /// </summary>
        public void ComposeAdditiveOverBindPose(FrameBone[] bones, Skeleton skeleton)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var bindPose = skeleton.Bones[i];
                var delta = GetAdditiveDelta(i, bones[i]);

                bones[i] = new FrameBone(bindPose.Position + delta.Position, 1f + delta.Scale, bindPose.Angle * delta.Angle);
            }
        }

        /// <summary>
        /// Decodes animation data for the specified frame.
        /// </summary>
        public abstract void DecodeFrame(Frame outFrame);

        /// <summary>
        /// Determines whether this animation has movement data.
        /// </summary>
        public abstract bool HasMovementData();

        /// <summary>
        /// Returns interpolated root motion data at the specified time.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(float time);

        /// <summary>
        /// Returns interpolated root motion data at the specified frame.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(int frame);

        /// <summary>
        /// The root motion advanced through between two playback times, as a rigid transform (yaw about Z
        /// plus translation) to compose onto whatever the animation drives.
        /// </summary>
        /// <remarks>
        /// Looping is unrolled rather than replayed: a step crossing the loop point runs out its cycle and
        /// carries on from the start of the next. Samples are the root's transform per frame rather than a
        /// delta from its start, and composing them this way cancels out the clip's own starting offset.
        /// Stepping backwards gives the forward step undone.
        /// </remarks>
        public Matrix4x4 GetRootMotionDelta(float fromTime, float toTime)
        {
            if (!HasMovementData() || CycleDuration <= 0f || fromTime == toTime)
            {
                return Matrix4x4.Identity;
            }

            if (toTime < fromTime)
            {
                return Matrix4x4.Invert(GetRootMotionDelta(toTime, fromTime), out var backwards)
                    ? backwards
                    : Matrix4x4.Identity;
            }

            var (fromCycle, fromFrame, fromRemainder) = GetCyclePosition(fromTime);
            var (toCycle, toFrame, toRemainder) = GetCyclePosition(toTime);

            if (!Matrix4x4.Invert(GetRootMotionAt(fromFrame, fromRemainder), out var delta))
            {
                return Matrix4x4.Identity;
            }

            if (fromCycle == toCycle)
            {
                return delta * GetRootMotionAt(toFrame, toRemainder);
            }

            if (!Matrix4x4.Invert(GetRootMotionAt(0, 0f), out var inverseCycleStart))
            {
                return Matrix4x4.Identity;
            }

            // Run out the starting cycle, add any skipped whole, then pick up from the start of the last one.
            var cycleEnd = GetRootMotionAt(CycleFrames, 0f);
            delta *= cycleEnd;

            for (var skipped = toCycle - fromCycle; skipped > 1; skipped--)
            {
                delta *= inverseCycleStart * cycleEnd;
            }

            return delta * inverseCycleStart * GetRootMotionAt(toFrame, toRemainder);
        }

        /// <summary>
        /// The root's transform at a frame within one cycle, interpolated towards the frame after it the same
        /// way the frame cache interpolates the pose so the two stay in step.
        /// </summary>
        private Matrix4x4 GetRootMotionAt(int frame, float remainder)
        {
            var movement = AnimationMovement.Lerp(
                GetMovementOffsetData(frame),
                GetMovementOffsetData(Math.Min(frame + 1, CycleFrames)),
                remainder
            );

            return Matrix4x4.CreateRotationZ(float.DegreesToRadians(movement.Angle))
                * Matrix4x4.CreateTranslation(movement.Position);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the animation name.
        /// </remarks>
        public override string ToString()
        {
            return Name;
        }
    }
}
