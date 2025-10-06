namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    [Flags]
    public enum ChoreoFlags
    {
#pragma warning disable CS1591
        ResumeCondition = 1,
        LockBodyFacing = 2,
        FixedLength = 4,
        IsActive = 8,
        ForceShortMovement = 16,
        PlayOverScript = 32,
#pragma warning restore CS1591
    }
}
