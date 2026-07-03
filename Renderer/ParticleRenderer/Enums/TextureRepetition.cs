namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// How a cable renderer's texture repeat count is interpreted along the rope.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/TextureRepetitionMode_t">TextureRepetitionMode_t</seealso>
    public enum TextureRepetitionMode
    {
        /// <summary>The repeat count applies per particle segment.</summary>
        TEXTURE_REPETITION_PARTICLE = 0,
        /// <summary>The repeat count is spread over the whole path.</summary>
        TEXTURE_REPETITION_PATH = 1,
    }
}
