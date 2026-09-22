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

        /// <summary>
        /// Gets the master post-process volume: the unbound volume that is the base state everywhere. The
        /// first enabled one of the volumes marked master.
        /// </summary>
        public ScenePostProcessVolume? MasterPostProcessVolume => masterVolumes.Find(static volume => volume.IsEnabled);

        private readonly List<ScenePostProcessVolume> masterVolumes = [];

        /// <summary>
        /// env_tonemap_controller is a legacy entity (S1) that still has functionality, so we want to account for it.
        /// The way this works is a little unclear, even after quite a bit of testing, but I think an env_tonemap_controller
        /// existing in the level overrides the post process volume regardless of if it's set as master.
        /// TODO: Test if this applies to post process volumes
        /// </summary>
        public SceneTonemapController? MasterTonemapController { get; set; }

        private bool isTonemapControllerMarkedMaster;

        /// <summary>
        /// Registers an <c>env_tonemap_controller</c>. The last one marked master becomes
        /// <see cref="MasterTonemapController"/>, or the first one registered when none is.
        /// </summary>
        /// <param name="controller">The controller to register.</param>
        /// <param name="isMaster">Whether the entity is marked master.</param>
        public void AddTonemapController(SceneTonemapController controller, bool isMaster)
        {
            if (isMaster)
            {
                MasterTonemapController = controller;
                isTonemapControllerMarkedMaster = true;
            }
            else if (!isTonemapControllerMarkedMaster)
            {
                MasterTonemapController ??= controller;
            }
        }

        /// <summary>Gets the post-processing state computed for the current frame.</summary>
        public PostProcessState CurrentState { get; private set; } = new();

        /// <summary>Gets the LUTs contributing this frame, strongest last; the remainder up to weight one is the neutral LUT.</summary>
        public List<WeightedLut> ActiveLuts { get; } = [];

        /// <summary>
        /// Registers a post-process volume, disabled ones included so they can be enabled later. Only the
        /// first enabled master volume applies; the rest apply by camera containment.
        /// </summary>
        /// <param name="postProcess">The post-process volume to register.</param>
        public void AddPostProcessVolume(ScenePostProcessVolume postProcess)
        {
            ArgumentNullException.ThrowIfNull(postProcess);

            if (postProcess.IsMaster)
            {
                masterVolumes.Add(postProcess);
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

            if (MasterPostProcessVolume is { } master)
            {
                ApplyVolume(ref newState, master, 1f);
            }

            foreach (var volume in PostProcessVolumes)
            {
                // A disabled volume fades out like one the camera left
                var inside = volume.IsEnabled
                    && volume.Collider != null
                    && volume.Collider.ContainsPoint(camera.Location);

                var targetWeight = inside ? 1f : 0f;
                var fadeStep = volume.FadeTime > 0f ? deltaTime / volume.FadeTime : 1f;

                volume.Weight = MathUtils.Approach(volume.Weight, targetWeight, fadeStep);

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
