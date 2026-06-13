namespace ValveResourceFormat
{
    /// <summary>
    /// Particle field identifiers.
    /// </summary>
    public enum ParticleField
    {
        /// <summary>World-space position of the particle.</summary>
        Position,
        /// <summary>Total lifetime duration in seconds.</summary>
        LifeDuration,
        /// <summary>World-space position from the previous frame, used for velocity.</summary>
        PositionPrevious,
        /// <summary>Visual radius of the particle.</summary>
        Radius,
        /// <summary>Roll rotation angle in radians.</summary>
        Roll,
        /// <summary>Roll rotation speed in degrees per second.</summary>
        RollSpeed,
        /// <summary>RGB color of the particle.</summary>
        Color,
        /// <summary>Opacity in the range [0, 1].</summary>
        Alpha,
        /// <summary>System time at which the particle was created.</summary>
        CreationTime,
        /// <summary>Primary sprite sheet sequence index.</summary>
        SequenceNumber,
        /// <summary>Trail length multiplier.</summary>
        TrailLength,
        /// <summary>Unique particle identifier.</summary>
        ParticleId,
        /// <summary>Yaw rotation angle in radians.</summary>
        Yaw,
        /// <summary>Secondary sprite sheet sequence index.</summary>
        SecondSequenceNumber,
        /// <summary>Index of the hitbox associated with this particle.</summary>
        HitboxIndex,
        /// <summary>World-space offset position relative to a hitbox.</summary>
        HitboxOffsetPosition,
        /// <summary>Alternate alpha value used by some operators and renderers.</summary>
        AlphaAlternate,
        /// <summary>General-purpose scratch vector field.</summary>
        ScratchVector,
        /// <summary>General-purpose scratch float field (index 0).</summary>
        ScratchFloat,
        /// <summary>Reserved field; no active use.</summary>
        NoneDisabled,
        /// <summary>Pitch rotation angle in radians.</summary>
        Pitch,
        /// <summary>Surface normal direction of the particle.</summary>
        Normal,
        /// <summary>RGB color used for glow effects.</summary>
        GlowRgb,
        /// <summary>Opacity of the glow effect.</summary>
        GlowAlpha,
        /// <summary>Pointer to the scene object associated with this particle.</summary>
        SceneObjectPointer,
        /// <summary>Pointer to a model helper object.</summary>
        ModelHelperPointer,
        /// <summary>General-purpose scratch float field (index 1).</summary>
        ScratchFloat1,
        /// <summary>General-purpose scratch float field (index 2).</summary>
        ScratchFloat2,
        /// <summary>Pointer to a second scene object associated with this particle.</summary>
        SceneObjectPointer2,
        /// <summary>Reference-counted pointer scratch field.</summary>
        RefCountedPointer,
        /// <summary>Second general-purpose scratch vector field.</summary>
        ScratchVector2,
        /// <summary>Bone indices for skinned mesh attachment.</summary>
        BoneIndices,
        /// <summary>Bone weights for skinned mesh attachment.</summary>
        BoneWeights,
        /// <summary>Index of this particle's parent in a parent-child hierarchy.</summary>
        ParentParticleIndex,
        /// <summary>Scale factor applied to forces acting on this particle.</summary>
        ForceScale,
        /// <summary>Pointer to a second model helper object.</summary>
        ModelHelperPointer2,
        /// <summary>Pointer to a third model helper object.</summary>
        ModelHelperPointer3,
        /// <summary>Pointer to a fourth model helper object.</summary>
        ModelHelperPointer4,
        /// <summary>Manually set animation frame index.</summary>
        ManualAnimationFrame,
        /// <summary>Shader-specific extra data field 1.</summary>
        ShaderExtraData1,
        /// <summary>Shader-specific extra data field 2.</summary>
        ShaderExtraData2,
    }
}
