namespace ValveResourceFormat.Particles
{
    /// <summary>Whether a renderer's particles are fogged.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleFogType_t">ParticleFogType_t</seealso>
    public enum ParticleFogType
    {
        /// <summary>Fogged when the game fogs particles by default.</summary>
        PARTICLE_FOG_GAME_DEFAULT = 0,
        /// <summary>Always fogged.</summary>
        PARTICLE_FOG_ENABLED = 1,
        /// <summary>Never fogged.</summary>
        PARTICLE_FOG_DISABLED = 2,
    }
}
