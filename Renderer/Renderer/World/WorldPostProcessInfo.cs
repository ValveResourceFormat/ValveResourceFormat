using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>One color correction LUT contributing to the current frame, with its blend weight.</summary>
    public readonly record struct WeightedLut(RenderTexture Lut, float Weight, int Dimensions);

    /// <summary>
    /// Manages post-processing volumes and current post-processing state for the scene.
    /// </summary>
    public class WorldPostProcessInfo()
    {
        /// <summary>The most volume LUTs that can be weighted together in one frame, matching the game's combine shader.</summary>
        public const int MaxBlendedLuts = 4;

        private const float MinWeight = 0.001f;

        /// <summary>Gets the non-master post-process volumes, applied while the camera is inside them.</summary>
        public List<ScenePostProcessVolume> PostProcessVolumes { get; } = [];

        /// <summary>Gets or sets the master post-process volume: the unbound volume that is the base state everywhere.</summary>
        public ScenePostProcessVolume? MasterPostProcessVolume { get; set; }

        /// <summary>
        /// env_tonemap_controller is a legacy entity (S1) that still has functionality, so we want to account for it.
        /// The way this works is a little unclear, even after quite a bit of testing, but I think an env_tonemap_controller
        /// existing in the level overrides the post process volume regardless of if it's set as master.
        /// TODO: Test if this applies to post process volumes
        /// </summary>
        public SceneTonemapController? MasterTonemapController { get; set; }

        /// <summary>Gets the post-processing state computed for the current frame.</summary>
        public PostProcessState CurrentState { get; private set; } = new();

        /// <summary>Gets the LUTs contributing this frame, strongest last; the remainder up to weight one is the neutral LUT.</summary>
        public List<WeightedLut> ActiveLuts { get; } = [];

        /// <summary>
        /// Registers a post-process volume. Only the first master volume is retained; the rest apply by
        /// camera containment.
        /// </summary>
        /// <param name="postProcess">The post-process volume to register.</param>
        public void AddPostProcessVolume(ScenePostProcessVolume postProcess)
        {
            if (postProcess.StartDisabled)
            {
                return;
            }

            if (postProcess.IsMaster)
            {
                // If there are multiple master volumes, S2 only takes the first one
                MasterPostProcessVolume ??= postProcess;
                // if it's marked as master but not the first master volume, it's entirely ignored
            }
            else
            {
                PostProcessVolumes.Add(postProcess);
            }
        }

        /// <summary>
        /// Recalculates <see cref="CurrentState"/>: the master volume is the base, and every non-master
        /// volume containing the camera crossfades on top of it over its fade time.
        /// </summary>
        /// <param name="camera">The active camera, tested against each volume's collider.</param>
        /// <param name="deltaTime">Elapsed time in seconds since the last frame, driving the crossfades.</param>
        public void UpdatePostProcessing(Camera camera, float deltaTime)
        {
            var newState = PostProcessState.Default;
            ActiveLuts.Clear();

            if (MasterPostProcessVolume != null)
            {
                ApplyVolume(ref newState, MasterPostProcessVolume, 1f);
            }

            foreach (var volume in PostProcessVolumes)
            {
                var inside = volume.Collider != null
                    && volume.Collider.ContainsPoint(camera.Location);

                var targetWeight = inside ? 1f : 0f;
                var fadeStep = volume.FadeTime > 0f ? deltaTime / volume.FadeTime : 1f;

                volume.Weight = float.Clamp(volume.Weight + float.Clamp(targetWeight - volume.Weight, -fadeStep, fadeStep), 0f, 1f);

                if (volume.Weight > MinWeight)
                {
                    ApplyVolume(ref newState, volume, volume.Weight);
                }
            }

            // A later volume at full weight replaces everything under it, so earlier contributions
            // scale down by what the volumes above them leave over.
            var remainder = 1f;
            for (var i = ActiveLuts.Count - 1; i >= 0; i--)
            {
                var entry = ActiveLuts[i];
                ActiveLuts[i] = entry with { Weight = entry.Weight * remainder };
                remainder *= 1f - entry.Weight;
            }

            ActiveLuts.RemoveAll(static lut => lut.Weight <= MinWeight);

            if (ActiveLuts.Count > MaxBlendedLuts)
            {
                // The combine is a weighted sum, so order does not matter; drop the weakest contributors
                ActiveLuts.Sort(static (a, b) => a.Weight.CompareTo(b.Weight));
                ActiveLuts.RemoveRange(0, ActiveLuts.Count - MaxBlendedLuts);
            }

            newState.NumLutsActive = ActiveLuts.Count;

            // env_tonemap_controller overrides the settings
            if (MasterTonemapController != null)
            {
                newState.ExposureSettings = MasterTonemapController.ControllerExposureSettings;
            }

            CurrentState = newState;
        }

        private void ApplyVolume(ref PostProcessState state, ScenePostProcessVolume volume, float weight)
        {
            if (volume.HasTonemap)
            {
                state.TonemapSettings = TonemapSettings.BlendTonemapSettings(weight, state.TonemapSettings, volume.PostProcessTonemapSettings);
            }

            if (volume.UseExposure)
            {
                state.ExposureSettings = ExposureSettings.Blend(weight, state.ExposureSettings, volume.ExposureSettings);
            }

            if (volume.HasBloom)
            {
                // With no bloom under this volume, fade in from the volume's own settings at zero strength
                var baseBloom = state.HasBloom
                    ? state.BloomSettings
                    : volume.BloomSettings with { AddBloomStrength = 0f, ScreenBloomStrength = 0f, BlurBloomStrength = 0f };

                state.BloomSettings = BloomSettings.Blend(weight, baseBloom, volume.BloomSettings);
                state.HasBloom = true;
            }

            if (volume.ColorCorrectionLUT != null)
            {
                ActiveLuts.Add(new WeightedLut(volume.ColorCorrectionLUT, weight, volume.ColorCorrectionLutDimensions));
            }
        }
    }
}
