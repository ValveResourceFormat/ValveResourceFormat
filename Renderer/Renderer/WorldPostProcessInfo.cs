namespace ValveResourceFormat.Renderer
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

        public ScenePostProcessVolume? MasterPostProcessVolume { get; set; }
        /// <summary>
        /// env_tonemap_controller is a legacy entity (S1) that still has functionality, so we want to account for it.
        /// The way this works is a little unclear, even after quite a bit of testing, but I think an env_tonemap_controller
        /// existing in the level overrides the post process volume regardless of if it's set as master.
        /// TODO: Test if this applies to post process volumes
        /// </summary>
        public SceneTonemapController? MasterTonemapController { get; set; }

        // Current post processing state
        public PostProcessState CurrentState { get; private set; } = new();

        public void AddPostProcessVolume(ScenePostProcessVolume postProcess)
        {
            if (postProcess.IsMaster)
            {
                // If there are multiple master volumes, S2 only takes the first one
                MasterPostProcessVolume ??= postProcess;
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
            var newState = new PostProcessState();

            // For we SHOULD find the weight of each volume, then blend the values together, and finally blend the remaining values with the Master

            // instead we just take the master only
            if (MasterPostProcessVolume != null)
            {
                newState.ExposureSettings = MasterPostProcessVolume.ExposureSettings;
                newState.TonemapSettings = MasterPostProcessVolume.PostProcessTonemapSettings;

                if (MasterPostProcessVolume.ColorCorrectionLUT != null)
                {
                    newState.NumLutsActive += 1;
                    newState.ColorCorrectionLUT = MasterPostProcessVolume.ColorCorrectionLUT;
                    newState.ColorCorrectionLutDimensions = MasterPostProcessVolume.ColorCorrectionLutDimensions;
                }
            }

            // env_tonemap_controller overrides the settings
            if (MasterTonemapController != null)
            {
                newState.ExposureSettings = MasterTonemapController.ControllerExposureSettings;
            }

            CurrentState = newState;
        }
    }
}
