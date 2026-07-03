namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// Maps a <see cref="ParticleField"/> to the snapshot (.vsnap) attribute name it is stored under.
    /// Shared by the snapshot-reading initializer and operator.
    /// </summary>
    static class CPSnapshotFields
    {
        public static string? GetSnapshotAttributeName(ParticleField field) => field switch
        {
            ParticleField.Position => "position",
            ParticleField.LifeDuration => "lifespan",
            ParticleField.PositionPrevious => "velocity",
            ParticleField.Radius => "radius",
            ParticleField.Roll => "rotation",
            ParticleField.RollSpeed => "rotation_speed",
            ParticleField.Color => "color",
            ParticleField.Alpha => "opacity",
            ParticleField.CreationTime => "creation_time",
            ParticleField.SequenceNumber => "sequence_number",
            ParticleField.TrailLength => "trail_length",
            ParticleField.ParticleId => "particle_id",
            ParticleField.Yaw => "yaw",
            ParticleField.SecondSequenceNumber => "sequence_number1",
            ParticleField.HitboxIndex => "hitbox",
            ParticleField.HitboxOffsetPosition => "hitbox_offset",
            ParticleField.AlphaAlternate => "alpha2",
            ParticleField.ScratchVector => "scratch_vec",
            ParticleField.ScratchFloat => "scratch_float",
            ParticleField.Pitch => "pitch",
            ParticleField.Normal => "normal",
            ParticleField.GlowRgb => "glow_rgb",
            ParticleField.GlowAlpha => "glow_alpha",
            ParticleField.ForceScale => "force_scale",
            ParticleField.ManualAnimationFrame => "manual_animation_frame",
            ParticleField.ShaderExtraData1 => "shader_extra_data_1",
            ParticleField.ShaderExtraData2 => "shader_extra_data_2",
            _ => null,
        };
    }
}
