namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    [Flags]
    public enum ChoreoSpeakFlags
    {
        None = 0,
        UsingCombinedFile = 1,
        CombinedUsingGenderToken = 2,
        SuppressingCaptionAttenuation = 4,
        HardStopSpeakEvent = 8,
        VolumeMatchesEventRamp = 16
    }
}
