using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Drives a model's skeleton from whichever <see cref="AnimationPlayer"/> owns the animation being
    /// played: the model's own player, or the player of an external NM skeleton whose pose is remapped
    /// onto the model by bone name.
    /// </summary>
    public partial class AnimationController
    {
        private Action<Animation?, int> updateHandler = (_, __) => { };

        /// <summary>
        /// The skeleton being animated.
        /// </summary>
        public Skeleton Skeleton { get; }

        /// <summary>
        /// The skeleton skinning bind pose.
        /// </summary>
        public Matrix4x4[] BindPose { get; }

        /// <summary>
        /// The skeleton inverse bind pose.
        /// </summary>
        public Matrix4x4[] InverseBindPose { get; }

        /// <summary>
        /// The flattened worldspace transform of each bone, according to the current animation frame.
        /// </summary>
        public Matrix4x4[] Pose { get; }

        private readonly AnimationPlayer modelPlayer;
        private readonly Dictionary<string, ExternalSkeleton> externalSkeletons = [];

        // The player producing the current pose; playback members forward to it.
        private AnimationPlayer player;

        // Retargets the current external player's pose onto the model skeleton, or null while the
        // model player is active.
        private SkeletonRetargeter? externalRetargeter;

        /// <summary>Gets or sets the playback speed multiplier applied to the animation timestep.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// The parent animating transform.
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

        /// <summary>
        /// Optional resolver from an attachment/bone name to a world position, used to place the sounds of
        /// animation events. TODO: rework this.
        /// </summary>
        public Func<string, Vector3?>? ResolvePosition
        {
            get => modelPlayer.ResolvePosition;
            set
            {
                modelPlayer.ResolvePosition = value;

                foreach (var external in externalSkeletons.Values)
                {
                    external.Player.ResolvePosition = value;
                }
            }
        }

        /// <summary>Gets or sets whether animations should loop when reaching the end.</summary>
        public bool Looping { get; set; } = true;

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation => player.ActiveAnimation;

        /// <summary>Gets the frame cache used to retrieve and interpolate frames on the model skeleton.</summary>
        public AnimationFrameCache FrameCache => modelPlayer.FrameCache;

        /// <summary>Gets the decoded animation frame data for the current tick, or <see langword="null"/> when no animation is active.</summary>
        public Frame? AnimationFrame { get; private set; }

        /// <summary>
        /// Takes the root motion the last <see cref="Update"/> advanced through, as a rigid transform to
        /// compose onto whatever the animation drives, and clears it. Identity when playback did not advance.
        /// </summary>
        public Matrix4x4 ConsumeRootMotionDelta() => player.ConsumeRootMotionDelta();

        /// <summary>Gets or sets whether animation playback is paused. Changing the value forces a pose update.</summary>
        public bool IsPaused
        {
            get => player.IsPaused;
            set => player.IsPaused = value;
        }

        /// <summary>Gets or sets whether the active animation is composed over the bind pose.</summary>
        public bool ApplyAdditive
        {
            get => player.ApplyAdditive;
            set => player.ApplyAdditive = value;
        }

        /// <summary>Gets or sets the current frame index of the active animation.</summary>
        public int Frame
        {
            get => player.Frame;
            set => player.Frame = value;
        }

        /// <summary>Gets or sets the current playback time in seconds.</summary>
        public float Time
        {
            get => player.Time;
            set => player.Time = value;
        }

        /// <summary>Gets whether the active animation clip has finished playing.</summary>
        public bool ActiveClipFinished => player.ActiveClipFinished;

        /// <summary>Gets whether the current animation frame is the result of blending multiple clips together.</summary>
        public bool IsUsingMixer => player.IsUsingMixer;

        /// <summary>Gets the clips of the player currently driving the pose.</summary>
        public Dictionary<string, AnimationPlayer.PlaybackClip> Clips => player.Clips;

        /// <summary>
        /// Initializes a new <see cref="AnimationController"/> for the given skeleton and flex controllers,
        /// computing the bind pose and inverse bind pose matrices.
        /// </summary>
        /// <param name="skeleton">The skeleton whose bones define the rig.</param>
        /// <param name="flexControllers">The flex controllers used for facial/morph animation.</param>
        public AnimationController(Skeleton skeleton, FlexController[] flexControllers)
        {
            Skeleton = skeleton;
            BindPose = ComputeBindPose(skeleton);
            InverseBindPose = new Matrix4x4[skeleton.Bones.Length];
            Pose = BindPose.AsSpan().ToArray();

            foreach (var root in skeleton.Roots)
            {
                GetInverseBindPoseRecursive(root, Matrix4x4.Identity, InverseBindPose);
            }

            // The model player writes directly into our pose buffer.
            modelPlayer = new AnimationPlayer(skeleton, flexControllers, BindPose, Pose);
            player = modelPlayer;
        }

        private static Matrix4x4[] ComputeBindPose(Skeleton skeleton)
        {
            var bindPose = new Matrix4x4[skeleton.Bones.Length];

            foreach (var root in skeleton.Roots)
            {
                Skeleton.ComputeWorldSubtree(root, Matrix4x4.Identity, null, bindPose);
            }

            return bindPose;
        }

        /// <summary>
        /// Advances the animation by <paramref name="timeStep"/> seconds and recomputes bone poses.
        /// </summary>
        /// <param name="timeStep">Elapsed time in seconds since the last update.</param>
        /// <returns><see langword="true"/> if the pose was updated; <see langword="false"/> if nothing changed.</returns>
        public bool Update(float timeStep)
        {
            timeStep *= FrametimeMultiplier;

            // External skeletons are posed in their own space; Transform is applied during remapping below.
            if (!player.Update(timeStep, externalRetargeter == null ? Transform : Matrix4x4.Identity))
            {
                return false;
            }

            AnimationFrame = player.AnimationFrame;
            updateHandler(ActiveAnimation, Frame);

            if (externalRetargeter is { } retargeter)
            {
                Debug.Assert(player != modelPlayer, "Remapping from the model player would alias the pose buffer.");

                foreach (var root in Skeleton.Roots)
                {
                    if (root.IsProceduralCloth)
                    {
                        continue;
                    }

                    retargeter.RetargetSubtree(root, Transform, player.Pose, Pose);
                }
            }
            else if (AnimationFrame == null)
            {
                // The model player already wrote the bind pose into our buffer.
                return true;
            }

            ApplyClothRootPose();
            ApplyInverseKinematics();
            return true;
        }

        /// <summary>
        /// Poses procedural cloth roots rigidly from the cloth simulation root, matching the pin
        /// <see cref="GetSkinningMatrices"/> applies to their skinning matrices.
        /// </summary>
        private void ApplyClothRootPose()
        {
            var clothSimRoot = Skeleton.ClothSimulationRoot;
            if (clothSimRoot == null)
            {
                return;
            }

            var delta = InverseBindPose[clothSimRoot.Index] * Pose[clothSimRoot.Index];

            foreach (var root in Skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    Pose[root.Index] = root.BindPose * delta;
                }
            }
        }

        /// <summary>
        /// Sets the active animation, resets playback to frame zero, and clears the frame cache.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        public void SetAnimation(Animation? animation)
        {
            SetAnimation(animation, 0f);
        }

        /// <summary>
        /// Sets the active animation with a blend-in time for smooth transitions, playing it on the
        /// external skeleton it targets when it has one.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        /// <param name="blendTime">The time in seconds to blend from previous animations to the new animation.</param>
        public void SetAnimation(Animation? animation, float blendTime)
        {
            var newPlayer = modelPlayer;
            SkeletonRetargeter? newRetargeter = null;

            if (animation is ClipAnimation clipAnimation && externalSkeletons.TryGetValue(clipAnimation.Clip.SkeletonName, out var external))
            {
                newPlayer = external.Player;
                newRetargeter = external.Retargeter;
            }

            if (newPlayer != player)
            {
                newPlayer.IsPaused = player.IsPaused;
                player.ClearClips();
            }

            player = newPlayer;
            externalRetargeter = newRetargeter;

            player.SetAnimation(animation, blendTime, Looping);
            updateHandler(ActiveAnimation, -1);
        }

        /// <summary>Pauses playback and seeks to the last frame of the active animation.</summary>
        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = ActiveAnimation == null ? 0 : ActiveAnimation.FrameCount - 1;
        }

        /// <summary>
        /// Returns the animation frame for the current time, using exact frame lookup when paused or interpolation during playback.
        /// </summary>
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
        public Frame? GetFrame() => player.GetFrame();

        /// <summary>
        /// Registers a callback invoked each time the animation frame changes, receiving the active animation and frame index.
        /// </summary>
        /// <param name="handler">The callback to invoke on each animation update.</param>
        public void RegisterUpdateHandler(Action<Animation?, int> handler)
        {
            updateHandler = handler;
        }

        /// <summary>
        /// The player driving the pose when an external skeleton's animation is active, or
        /// <see langword="null"/> while the model's own skeleton is being animated.
        /// </summary>
        public AnimationPlayer? CurrentPlayer => player == modelPlayer ? null : player;

        /// <summary>
        /// An external NM skeleton the model can be animated from, and the retargeter mapping its
        /// poses back onto the model skeleton.
        /// </summary>
        /// <param name="Player">The player animating the external skeleton.</param>
        /// <param name="Retargeter">Retargets the external skeleton's poses onto the model skeleton.</param>
        public readonly record struct ExternalSkeleton(AnimationPlayer Player, SkeletonRetargeter Retargeter)
        {
            /// <summary>The external skeleton.</summary>
            public Skeleton Skeleton => Player.Skeleton;
        }

        /// <summary>
        /// Gets the external skeletons registered for playback, indexed by skeleton name.
        /// </summary>
        public IReadOnlyDictionary<string, ExternalSkeleton> ExternalSkeletons => externalSkeletons;

        /// <summary>
        /// Whether the animation can play correctly on this controller.
        /// </summary>
        public bool IsPlayable(Animation animation)
        {
            if (animation is ClipAnimation clipAnimation)
            {
                return externalSkeletons.ContainsKey(clipAnimation.TargetSkeletonName);
            }

            return true;
        }

        /// <summary>
        /// Registers an external skeleton animations can be played on, creating a bone remapping table.
        /// </summary>
        /// <param name="skeletonName">The name identifying the external skeleton.</param>
        /// <param name="skeleton">The external skeleton to register.</param>
        public void RegisterExternalSkeleton(string skeletonName, Skeleton skeleton)
        {
            var retargeter = new SkeletonRetargeter(Skeleton, skeleton);

            var bindPose = ComputeBindPose(skeleton);
            var externalPlayer = new AnimationPlayer(skeleton, [], bindPose, bindPose.AsSpan().ToArray())
            {
                ResolvePosition = ResolvePosition,
            };

            externalSkeletons[skeletonName] = new(externalPlayer, retargeter);
        }

        /// <summary>
        /// Registers a bone mask for per-bone transform weighting.
        /// </summary>
        /// <param name="name">The name of the bone mask.</param>
        /// <param name="boneWeights">Dictionary mapping bone names to weight values (0.0 to 1.0).</param>
        /// <param name="skeletonName">Optional external skeleton to register the mask on.</param>
        public void RegisterBoneMask(string name, Dictionary<string, float> boneWeights, string? skeletonName = null)
        {
            var target = skeletonName != null && externalSkeletons.TryGetValue(skeletonName, out var external)
                ? external.Player
                : modelPlayer;

            target.RegisterBoneMask(name, boneWeights);
        }

        /// <summary>
        /// Sets the blend weight for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="weight">The weight value (0.0 to 1.0).</param>
        /// <param name="restartIfNew">Whether to restart the animation if it's just now fading in.</param>
        public void SetAnimationWeight(string name, float weight, bool restartIfNew = false)
            => player.SetAnimationWeight(name, weight, restartIfNew);

        /// <summary>
        /// Sets properties for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="time">Optional playback time to set.</param>
        /// <param name="looping">Optional looping flag to set.</param>
        /// <param name="boneMask">Optional bone mask name to set.</param>
        public void SetAnimationProperties(string name, float? time = null, bool? looping = null, string? boneMask = null)
            => player.SetAnimationProperties(name, time, looping, boneMask);

        /// <summary>
        /// Recursively computes the inverse bind pose matrix for each bone in the hierarchy.
        /// </summary>
        /// <param name="bone">The current bone to process.</param>
        /// <param name="parent">The accumulated inverse bind pose from the parent.</param>
        /// <param name="boneMatrices">The output array to store computed inverse bind pose matrices.</param>
        private static void GetInverseBindPoseRecursive(Bone bone, Matrix4x4 parent, Span<Matrix4x4> boneMatrices)
        {
            boneMatrices[bone.Index] = parent * bone.InverseBindPose;

            foreach (var child in bone.Children)
            {
                GetInverseBindPoseRecursive(child, boneMatrices[bone.Index], boneMatrices);
            }
        }

        /// <summary>
        /// Get bone matrices in bindpose space.
        /// Bones that do not move from the original location will have an identity matrix.
        /// Thus there will be no transformation in the vertex shader.
        /// </summary>
        public void GetSkinningMatrices(Span<Matrix4x4> modelBones)
        {
            for (var i = 0; i < Pose.Length; i++)
            {
                modelBones[i] = InverseBindPose[i] * Pose[i];
            }

            // Copy procedural cloth node transforms from an animated root bone
            var clothSimRoot = Skeleton.ClothSimulationRoot;
            if (clothSimRoot is not null)
            {
                foreach (var clothNode in Skeleton.Roots)
                {
                    if (clothNode.IsProceduralCloth)
                    {
                        modelBones[clothNode.Index] = modelBones[clothSimRoot.Index];
                    }
                }
            }
        }
    }
}
