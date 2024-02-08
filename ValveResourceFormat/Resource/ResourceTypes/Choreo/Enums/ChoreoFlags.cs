namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    [Flags]
    public enum ChoreoFlags
    {
        ResumeCondition = 1,
        LockBodyFacing = 2,
        FixedLength = 4,
        IsActive = 8,
        ForceShortMovement = 16,
        PlayOverScript = 32,
    }
}
