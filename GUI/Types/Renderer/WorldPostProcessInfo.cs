namespace GUI.Types.Renderer;

partial class Scene
{
    public class WorldPostProcessInfo()
    {
        // This may seem like a really stupid structure, but because of
        // lack of collision detection in S2V, we can only handle one
        // PPV at once. This means we're only using the master volume.
        // Despite this, this system is generally built with the correct many-volumes
        // behavior in mind, to minimize changes if implemented.

        // So, currently, this list won't be used.
        //public List<ScenePostProcessVolume> PostProcessVolumes { get; set; } = [];

        public ScenePostProcessVolume MasterPostProcessVolume { get; set; }
        /// <summary>
        /// env_tonemap_controller is a legacy entity (S1) that still has functionality, so we want to account for it.
        /// The way this works is a little unclear, even after quite a bit of testing, but I think an env_tonemap_controller
        /// existing in the level overrides the post process volume regardless of if it's set as master.
        /// TODO: Test if this applies to post process volumes
        /// </summary>
        public SceneTonemapController MasterTonemapController { get; set; }

        // Current post processing state
        public PostProcessState CurrentState = new();
        public float CustomExposure = -1;

        public void AddPostProcessVolume(ScenePostProcessVolume postProcess)
        {
            if (postProcess.IsMaster)
            {
                // If there are multiple master volumes, S2 only takes the first one
                if (MasterPostProcessVolume == null)
                {
                    MasterPostProcessVolume = postProcess;
                }
                // if it's marked as master but not the first master volume, it's entirely ignored
            }
            else
            {
                // Currently unused as we currently don't consider any volume but the master volume
                //PostProcessVolumes.Add(postProcess);
            }
        }

        // This is where we would update and blend between volumes per frame.
        // Because we only take the master volume currently, we don't need to do this yet.
        // The Camera variable is present but not referenced as we currently can't check collision detection.
        // Also, this needs to be guaranteed to run every frame, and before buffers
        public void UpdatePostProcessing(Camera camera)
        {
            // Recalculate post process state
            CurrentState = new PostProcessState();

            // For we SHOULD find the weight of each volume, then blend the values together, and finally blend the remaining values with the Master

            // instead we just take the master only
            if (MasterPostProcessVolume != null)
            {
                CurrentState.ExposureSettings = MasterPostProcessVolume.ExposureSettings;
                CurrentState.TonemapSettings = MasterPostProcessVolume.PostProcessTonemapSettings;

                if (MasterPostProcessVolume.ColorCorrectionLUT != null)
                {
                    CurrentState.NumLutsActive += 1;
                    CurrentState.ColorCorrectionLUT = MasterPostProcessVolume.ColorCorrectionLUT;
                    CurrentState.ColorCorrectionLutDimensions = MasterPostProcessVolume.ColorCorrectionLutDimensions;
                }
            }

            // env_tonemap_controller overrides the settings
            if (MasterTonemapController != null)
            {
                CurrentState.ExposureSettings = MasterTonemapController.ControllerExposureSettings;
            }
        }

        public float CalculateTonemapScalar()
        {
            var exposure = 1.0f;

            if (CustomExposure != -1)
            {
                return CustomExposure;
            }

            // Don't actually compute auto-exposure, but at least limit it to their bounds if the min/max excludes 1
            if (CurrentState.ExposureSettings.AutoExposureEnabled)
            {
                if (CurrentState.ExposureSettings.ExposureMin > 1.0f)
                {
                    exposure = CurrentState.ExposureSettings.ExposureMin;
                }
                if (CurrentState.ExposureSettings.ExposureMax < 1.0f)
                {
                    exposure = CurrentState.ExposureSettings.ExposureMax;
                }

                // Apply exposure compensation
                exposure *= MathF.Pow(2.0f, CurrentState.ExposureSettings.ExposureCompensation);
            }

            return exposure;
        }

        //public void SetTonemapViewConstants(ViewConstants viewConstants)
        //{
        //    viewConstants.ToneMapScalarLinear = CalculateTonemapScalar();
        //}
    }
}
