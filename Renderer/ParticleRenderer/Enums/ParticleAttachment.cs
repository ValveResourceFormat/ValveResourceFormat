namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Attachment modes for particle systems relative to entities.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/ParticleAttachment_t">ParticleAttachment_t</seealso>
    public enum ParticleAttachment
    {
        /// <summary>Invalid attachment.</summary>
        PATTACH_INVALID = -1,
        /// <summary>Attached at the entity's absolute origin.</summary>
        PATTACH_ABSORIGIN = 0,
        /// <summary>Attached at the entity's absolute origin, following the entity.</summary>
        PATTACH_ABSORIGIN_FOLLOW = 1,
        /// <summary>Attached at a custom origin.</summary>
        PATTACH_CUSTOMORIGIN = 2,
        /// <summary>Attached at a custom origin, following it.</summary>
        PATTACH_CUSTOMORIGIN_FOLLOW = 3,
        /// <summary>Attached at a named attachment point.</summary>
        PATTACH_POINT = 4,
        /// <summary>Attached at a named attachment point, following the entity.</summary>
        PATTACH_POINT_FOLLOW = 5,
        /// <summary>Attached at the entity's eye position, following the entity.</summary>
        PATTACH_EYES_FOLLOW = 6,
        /// <summary>Attached overhead, following the entity.</summary>
        PATTACH_OVERHEAD_FOLLOW = 7,
        /// <summary>Attached at the world origin.</summary>
        PATTACH_WORLDORIGIN = 8,
        /// <summary>Attached at the root bone, following the entity.</summary>
        PATTACH_ROOTBONE_FOLLOW = 9,
        /// <summary>Attached at the render origin, following the entity.</summary>
        PATTACH_RENDERORIGIN_FOLLOW = 10,
        /// <summary>Attached at the main view position.</summary>
        PATTACH_MAIN_VIEW = 11,
        /// <summary>Attached at a water wake position.</summary>
        PATTACH_WATERWAKE = 12,
        /// <summary>Attached at the center of the entity, following it.</summary>
        PATTACH_CENTER_FOLLOW = 13,
        /// <summary>Custom game state attachment slot 1.</summary>
        PATTACH_CUSTOM_GAME_STATE_1 = 14,
        /// <summary>Attached at a health bar position.</summary>
        PATTACH_HEALTHBAR = 15,
    };
}
