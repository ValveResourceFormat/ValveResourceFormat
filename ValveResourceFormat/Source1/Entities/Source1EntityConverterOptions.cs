namespace ValveResourceFormat.Source1.Entities
{
    /// <summary>
    /// Options for <see cref="Source1EntityConverter"/>.
    /// </summary>
    internal sealed class Source1EntityConverterOptions
    {
        /// <summary>
        /// Gets whether Source 1 classnames are replaced with the Source 2 classes the renderer implements,
        /// such as <c>light</c> with <c>light_omni</c>. The original is kept in <c>source1_classname</c> either way.
        /// </summary>
        public bool RemapClassNames { get; init; } = true;

        /// <summary>
        /// Gets whether converted lights are written as dynamic (<c>directlight 2</c>), which is what lights models
        /// when no Source 2 lightmap exists. Otherwise they are written as baked (<c>directlight 1</c>).
        /// </summary>
        public bool DynamicLights { get; init; } = true;

        /// <summary>
        /// Gets whether the map is lit with its HDR lighting, which makes lights read their HDR keys such as
        /// <c>_lightHDR</c>. Otherwise only the LDR keys are read.
        /// </summary>
        public bool HdrLighting { get; init; }
    }
}
