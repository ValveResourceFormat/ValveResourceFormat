namespace ValveResourceFormat.Particles
{
    /// <summary>The weights of a blur kernel.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/BlurFilterType_t">BlurFilterType_t</seealso>
    public enum BlurFilterType
    {
        /// <summary>Gaussian weights.</summary>
        BLURFILTER_GAUSSIAN = 0,
        /// <summary>Equal weights.</summary>
        BLURFILTER_BOX = 1,
    }
}
