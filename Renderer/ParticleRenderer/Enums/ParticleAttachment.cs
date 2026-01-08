namespace ValveResourceFormat.Renderer.Particles
{
    public enum ParticleAttachment // ParticleAttachment_t
    {
        PATTACH_INVALID = -1,
        PATTACH_ABSORIGIN = 0,
        PATTACH_ABSORIGIN_FOLLOW = 1,
        PATTACH_CUSTOMORIGIN = 2,
        PATTACH_CUSTOMORIGIN_FOLLOW = 3,
        PATTACH_POINT = 4,
        PATTACH_POINT_FOLLOW = 5,
        PATTACH_EYES_FOLLOW = 6,
        PATTACH_OVERHEAD_FOLLOW = 7,
        PATTACH_WORLDORIGIN = 8,
        PATTACH_ROOTBONE_FOLLOW = 9,
        PATTACH_RENDERORIGIN_FOLLOW = 10,
        PATTACH_MAIN_VIEW = 11,
        PATTACH_WATERWAKE = 12,
        PATTACH_CENTER_FOLLOW = 13,
        PATTACH_CUSTOM_GAME_STATE_1 = 14,
        PATTACH_HEALTHBAR = 15,
    };
}
