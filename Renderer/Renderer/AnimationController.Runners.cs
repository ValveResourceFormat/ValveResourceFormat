using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationController
    {
        // Chosen once per SetAnimation. Every stateful public member forwards through this single
        // reference, so playback state cannot be read from or written to the wrong backend.
        private PlaybackRunner runner;

        private readonly DirectPlaybackRunner directRunner;
        private readonly Dictionary<string, RetargetPlaybackRunner> retargetRunners = [];

        /// <summary>
        /// Produces the pose for one playback arrangement: directly on the model skeleton, or
        /// retargeted from an external skeleton driven by a nested controller.
        /// </summary>
        private abstract class PlaybackRunner
        {
            public abstract Animation? ActiveAnimation { get; }
            public abstract int Frame { get; set; }
            public abstract float Time { get; set; }
            public abstract bool ApplyAdditive { get; set; }
            public abstract bool ActiveClipFinished { get; }
            public abstract void SetAnimation(Animation? animation, float blendTime);
            public abstract void SetAnimationWeight(string name, float weight, bool restartIfNew);
            public abstract void SetAnimationProperties(string name, float? time, bool? looping, string? boneMask);

            /// <summary>
            /// Advances playback and writes the owning controller's pose, including cloth root and
            /// inverse kinematics. Returns whether anything was updated and the frame to publish as
            /// <see cref="AnimationFrame"/>.
            /// </summary>
            public abstract bool Update(float timeStep, out Frame? animationFrame);
        }

        /// <summary>
        /// Plays animations decoded directly on the model skeleton through the clip mixer.
        /// </summary>
        private sealed class DirectPlaybackRunner(AnimationController controller) : PlaybackRunner
        {
            private bool applyAdditive;

            public override Animation? ActiveAnimation => controller.activeClip?.Animation;

            public override int Frame
            {
                get => controller.activeClip?.Frame ?? 0;
                set => controller.activeClip?.Frame = value;
            }

            public override float Time
            {
                get => controller.activeClip?.Time ?? 0f;
                set => controller.activeClip?.Time = value;
            }

            public override bool ApplyAdditive
            {
                get => applyAdditive;
                set => applyAdditive = value;
            }

            public override bool ActiveClipFinished
                => controller.activeClip is { Looping: false, IsPaused: true };

            public override void SetAnimation(Animation? animation, float blendTime)
            {
                controller.FrameCache.PurgeCache();

                // Animation.IsAdditive already resolves AG2 (clip flag) and AG1 (animation graph) additive.
                applyAdditive = animation?.IsAdditive ?? false;

                if (animation != null)
                {
                    controller.TransitionToClip(animation, blendTime);
                }
                else
                {
                    controller.activeClip = null;
                }
            }

            /// <summary>
            /// Clears the mixer state when another runner takes over, so a later switch back cannot
            /// blend from a stale clip.
            /// </summary>
            public void ClearClips()
            {
                controller.activeClip = null;
                controller.previousClip = null;
                controller.clips.Clear();
            }

            public override void SetAnimationWeight(string name, float weight, bool restartIfNew)
            {
                if (controller.clips.TryGetValue(name, out var clip))
                {
                    var wasZero = clip.Weight == 0f;
                    clip.Weight = weight;

                    if (restartIfNew && wasZero && weight > 0f)
                    {
                        clip.Time = 0f;
                        clip.IsPaused = false;
                    }
                }
            }

            public override void SetAnimationProperties(string name, float? time, bool? looping, string? boneMask)
            {
                if (controller.clips.TryGetValue(name, out var clip))
                {
                    if (time.HasValue)
                    {
                        clip.Time = time.Value;
                        clip.IsPaused = false;
                    }

                    if (looping.HasValue)
                    {
                        clip.Looping = looping.Value;
                    }

                    if (boneMask != null)
                    {
                        clip.BoneMask = boneMask;
                    }
                }
            }

            public override bool Update(float timeStep, out Frame? animationFrame)
            {
                timeStep *= controller.FrametimeMultiplier;

                if (!controller.IsPaused && controller.activeClip != null)
                {
                    controller.UpdateClips(timeStep);
                }

                animationFrame = controller.GetBlendedFrame(out var usingMixer);
                controller.IsUsingMixer = usingMixer;

                if (animationFrame == null)
                {
                    controller.BindPose.AsSpan().CopyTo(controller.Pose);
                    return true;
                }

                // Additive clips are composed over the skeleton bind pose instead of applied as an
                // absolute pose. Whether an animation is additive is decided by Animation.IsAdditive
                // (AG2 clip flag or AG1 graph); the compose itself lives on Animation so the exporter
                // shares it.
                if (!usingMixer && applyAdditive && ActiveAnimation is { } activeAnimation)
                {
                    // Compose into the controller's own scratch frame so neither the frame cache nor
                    // the sampled frame is mutated. Only the bones are recomputed here, so carry the
                    // flex (Datas) and movement across as well.
                    var scratch = controller.AdditiveFrame;
                    animationFrame.Bones.CopyTo(scratch.Bones);
                    animationFrame.Datas.CopyTo(scratch.Datas);
                    scratch.Movement = animationFrame.Movement;
                    scratch.FrameIndex = animationFrame.FrameIndex;
                    animationFrame = scratch;

                    activeAnimation.ComposeAdditiveOverBindPose(animationFrame.Bones, controller.Skeleton);
                }

                foreach (var root in controller.Skeleton.Roots)
                {
                    if (root.IsProceduralCloth)
                    {
                        continue;
                    }

                    GetBoneMatricesRecursive(root, controller.Transform, animationFrame, controller.Pose);
                }

                controller.ApplyClothRootPose();
                controller.ApplyInverseKinematics();
                return true;
            }
        }

        /// <summary>
        /// Plays animation graph (NM) clips through a nested controller that owns the external
        /// skeleton, remapping its pose onto the model skeleton by bone name.
        /// </summary>
        private sealed class RetargetPlaybackRunner(AnimationController controller, SubController sub) : PlaybackRunner
        {
            /// <summary>Gets the sub-controller driving the external skeleton.</summary>
            public SubController Sub => sub;

            private AnimationController Handler => sub.Handler;

            public override Animation? ActiveAnimation => Handler.ActiveAnimation;

            public override int Frame
            {
                get => Handler.Frame;
                set => Handler.Frame = value;
            }

            public override float Time
            {
                get => Handler.Time;
                set => Handler.Time = value;
            }

            public override bool ApplyAdditive
            {
                get => Handler.ApplyAdditive;
                set => Handler.ApplyAdditive = value;
            }

            public override bool ActiveClipFinished => Handler.ActiveClipFinished;

            public override void SetAnimation(Animation? animation, float blendTime)
            {
                Handler.Looping = controller.Looping;
                Handler.FrametimeMultiplier = controller.FrametimeMultiplier;
                Handler.SetAnimation(animation, blendTime);
            }

            public override void SetAnimationWeight(string name, float weight, bool restartIfNew)
                => Handler.SetAnimationWeight(name, weight, restartIfNew);

            public override void SetAnimationProperties(string name, float? time, bool? looping, string? boneMask)
                => Handler.SetAnimationProperties(name, time, looping, boneMask);

            public override bool Update(float timeStep, out Frame? animationFrame)
            {
                animationFrame = null;

                // The nested controller applies the frametime multiplier itself in its own Update.
                Handler.IsPaused = controller.IsPaused;
                Handler.Looping = controller.Looping;
                Handler.FrametimeMultiplier = controller.FrametimeMultiplier;
                Handler.forceUpdate = controller.forceUpdate;

                var updated = Handler.Update(timeStep);
                controller.IsPaused = Handler.IsPaused;
                controller.forceUpdate = Handler.forceUpdate;

                if (!updated && !controller.forceUpdate)
                {
                    return false;
                }

                foreach (var root in controller.Skeleton.Roots)
                {
                    if (root.IsProceduralCloth)
                    {
                        continue;
                    }

                    ComputePoseRecursive(root, controller.Transform, controller.Pose);
                }

                controller.ApplyClothRootPose();
                controller.ApplyInverseKinematics();

                animationFrame = Handler.AnimationFrame;
                return true;
            }

            // A remapped model bone takes the external skeleton's live pose (including its cloth and
            // IK); unmapped bones follow their parent at bind pose.
            private void ComputePoseRecursive(Bone bone, Matrix4x4 parentTransform, Span<Matrix4x4> pose)
            {
                var remapIndex = sub.RemapTable[bone.Index];

                pose[bone.Index] = remapIndex != -1
                    ? Handler.Pose[remapIndex]
                    : bone.BindPose * parentTransform;

                foreach (var child in bone.Children)
                {
                    ComputePoseRecursive(child, pose[bone.Index], pose);
                }
            }
        }
    }
}
