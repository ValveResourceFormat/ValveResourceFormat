namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    /// <summary>
    /// Represents the type of a choreography event. Enum values DO NOT line up with the compiled values. Newer vcd versions are missing the camera event.
    /// </summary>
    public enum ChoreoEventType
    {
#pragma warning disable CS1591
        //"lookattransition" and "facetransition" events exist in the v9 faceposer, but they become corrupted/invalid events in the vcd when compiled. Did these exist on any version?
        Unspecified,
        Section,
        Expression,
        LookAt,
        MoveTo,
        Speak,
        Gesture,
        Sequence,
        Face,
        FireTrigger,
        FlexAnimation,
        SubScene,
        Loop,
        Interrupt,
        StopPoint,
        PermitResponses,
        Generic,
        Camera,
        Script,
        AnimgraphController,
        MoodBody,
        IKLockLeftArm,
        IKLockRightArm,
        NoBlink,
        IgnoreAI,
        HolsterWeapon,
        UnholsterWeapon,
        AimAt,
        IgnoreCollision,
        IgnoreLookAts,
#pragma warning restore CS1591
    }
}
