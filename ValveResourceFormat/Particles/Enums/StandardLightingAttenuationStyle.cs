namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a standard particle light falls off with distance.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/StandardLightingAttenuationStyle_t">StandardLightingAttenuationStyle_t</seealso>
    public enum StandardLightingAttenuationStyle
    {
        /// <summary>The range is the radius multiplier times the particle radius.</summary>
        LIGHT_STYLE_OLD = 0,
        /// <summary>The range is also scaled by the zero percent falloff.</summary>
        LIGHT_STYLE_NEW = 1,
    }
}
