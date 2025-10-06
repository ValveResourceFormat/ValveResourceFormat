namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    [Flags]
    public enum ChoreoSpeakFlags
    {
#pragma warning disable CS1591
        None = 0,
        UsingCombinedFile = 1,
        CombinedUsingGenderToken = 2,
        SuppressingCaptionAttenuation = 4,
        HardStopSpeakEvent = 8,
        VolumeMatchesEventRamp = 16
#pragma warning restore CS1591
    }
}
