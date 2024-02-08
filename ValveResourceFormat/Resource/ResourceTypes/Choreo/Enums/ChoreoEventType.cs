namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    public enum ChoreoEventType
    {
        //"lookattransition" and "facetransition" events exist in the v9 faceposer, but they become corrupted/invalid events in the vcd when compiled. Did these exist on any version?
        Invalid = -1,
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
        /// <summary>
        /// This is "camera" in vcd version 9, "script" in vcd version 17
        /// </summary>
        CameraOrScript,
        AnimgraphController,
        //Unknown value at 19. Does it exist?
        MoodBody = 20,
        IKLockLeftArm,
        IKLockRightArm,
        NoBlink,
        IgnoreAI,
        HolsterWeapon,
        UnholsterWeapon,
        AimAt,
        IgnoreCollision,
        IgnoreLookAts,
    }
}
