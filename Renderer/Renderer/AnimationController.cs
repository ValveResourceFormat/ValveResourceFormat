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

        // Model bone index -> current player's bone index, or null while the model player is active.
        private int[]? remapTable;

        /// <summary>Gets or sets the playback speed multiplier applied to the animation timestep.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// The parent animating transform.
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

        /// <summary>Gets or sets whether animations should loop when reaching the end.</summary>
        public bool Looping { get; set; } = true;

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation => player.ActiveAnimation;

        /// <summary>Gets the frame cache used to retrieve and interpolate frames on the model skeleton.</summary>
        public AnimationFrameCache FrameCache => modelPlayer.FrameCache;

        /// <summary>Gets the decoded animation frame data for the current tick, or <see langword="null"/> when no animation is active.</summary>
        public Frame? AnimationFrame { get; private set; }

        /// <summary>Gets or sets whether animation playback is paused. Changing the value forces a pose update.</summary>
        public bool IsPaused
        {
            get => player.IsPaused;
            set => player.IsPaused = value;
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
                FramePose.ComputeWorldSubtree(root, Matrix4x4.Identity, null, bindPose);
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
            if (!player.Update(timeStep, remapTable == null ? Transform : Matrix4x4.Identity))
            {
                return false;
            }

            AnimationFrame = player.AnimationFrame;
            updateHandler(ActiveAnimation, Frame);

            if (remapTable is { } remap)
            {
                Debug.Assert(player != modelPlayer, "Remapping from the model player would alias the pose buffer.");

                foreach (var root in Skeleton.Roots)
                {
                    if (root.IsProceduralCloth)
                    {
                        continue;
                    }

                    RemapPoseRecursive(root, Transform, remap, player.Pose, Pose);
                }
            }
            else if (AnimationFrame == null)
            {
                // The model player already wrote the bind pose into our buffer.
                return true;
            }

            ApplyInverseKinematics();
            return true;
        }

        /// <summary>
        /// Copies a source skeleton's world pose onto a model bone subtree: a mapped model bone takes
        /// its source bone's pose, an unmapped one follows its parent at bind pose.
        /// </summary>
        private static void RemapPoseRecursive(Bone bone, Matrix4x4 parentTransform, int[] remapTable, ReadOnlySpan<Matrix4x4> sourcePose, Span<Matrix4x4> pose)
        {
            var remapIndex = remapTable[bone.Index];

            pose[bone.Index] = remapIndex != -1
                ? sourcePose[remapIndex]
                : bone.BindPose * parentTransform;

            foreach (var child in bone.Children)
            {
                RemapPoseRecursive(child, pose[bone.Index], remapTable, sourcePose, pose);
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
            int[]? newRemapTable = null;

            if (animation is ClipAnimation clipAnimation && externalSkeletons.TryGetValue(clipAnimation.Clip.SkeletonName, out var external))
            {
                newPlayer = external.Player;
                newRemapTable = external.RemapTable;
            }

            if (newPlayer != player)
            {
                newPlayer.IsPaused = player.IsPaused;
                player.ClearClips();
            }

            player = newPlayer;
            remapTable = newRemapTable;

            player.SetAnimation(animation, blendTime, Looping);
            updateHandler(ActiveAnimation, -1);
        }

        /// <summary>
        /// Attaches an animation graph as the pose source, playing it on the player of the external
        /// skeleton the graph animates (registering that skeleton if needed). Pass <see langword="null"/>
        /// to detach the graph from the current player and return to clip playback.
        /// </summary>
        /// <param name="graph">The animation graph to play, or <see langword="null"/> to detach.</param>
        public void SetAnimationGraph(AnimationGraph? graph)
        {
            if (graph == null)
            {
                player.SetGraph(null);
                return;
            }

            if (!externalSkeletons.TryGetValue(graph.SkeletonName, out var external))
            {
                RegisterExternalSkeleton(graph.SkeletonName, graph.Skeleton);
                external = externalSkeletons[graph.SkeletonName];
            }

            var newPlayer = external.Player;

            if (newPlayer != player)
            {
                newPlayer.IsPaused = player.IsPaused;
                player.ClearClips();
                player.SetGraph(null);
            }

            player = newPlayer;
            remapTable = external.RemapTable;

            player.SetGraph(graph);
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
        /// An external NM skeleton the model can be animated from, and the bone mapping back onto it.
        /// </summary>
        /// <param name="Player">The player animating the external skeleton.</param>
        /// <param name="RemapTable">Bone index mapping from the model skeleton to the external one.</param>
        public readonly record struct ExternalSkeleton(AnimationPlayer Player, int[] RemapTable)
        {
            /// <summary>The external skeleton.</summary>
            public Skeleton Skeleton => Player.Skeleton;
        }

        /// <summary>
        /// Gets the external skeletons registered for playback, indexed by skeleton name.
        /// </summary>
        public IReadOnlyDictionary<string, ExternalSkeleton> ExternalSkeletons => externalSkeletons;

        /// <summary>
        /// Registers an external skeleton animations can be played on, creating a bone remapping table.
        /// </summary>
        /// <param name="skeletonName">The name identifying the external skeleton.</param>
        /// <param name="skeleton">The external skeleton to register.</param>
        public void RegisterExternalSkeleton(string skeletonName, Skeleton skeleton)
        {
            var sourceBoneCount = skeleton.Bones.Length;
            var destinationBoneCount = Skeleton.Bones.Length;

            var remap = new int[destinationBoneCount];
            var nameToIndex = new Dictionary<uint, int>(sourceBoneCount);

            for (var i = 0; i < sourceBoneCount; i++)
            {
                var name = skeleton.Bones[i].Name;
                nameToIndex[StringToken.Store(name)] = i;
            }

            for (var i = 0; i < destinationBoneCount; i++)
            {
                var name = Skeleton.Bones[i].Name;
                var hash = StringToken.Store(name);

                remap[i] = -1;

                if (nameToIndex.TryGetValue(hash, out var idx))
                {
                    remap[i] = idx;
                }
            }

            var bindPose = ComputeBindPose(skeleton);
            var externalPlayer = new AnimationPlayer(skeleton, [], bindPose, bindPose.AsSpan().ToArray());

            externalSkeletons[skeletonName] = new(externalPlayer, remap);
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
