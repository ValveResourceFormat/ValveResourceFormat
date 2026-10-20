namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// How a material blends with what is behind it.
    /// </summary>
    public enum MaterialBlendMode
    {
        /// <summary>Hides what is behind it.</summary>
        Opaque,

        /// <summary>Hides what is behind it where its opacity reaches the alpha test reference.</summary>
        AlphaTest,

        /// <summary>Blends over what is behind it by its opacity.</summary>
        Translucent,

        /// <summary>Adds its color, scaled by its opacity, to what is behind it.</summary>
        Additive,

        /// <summary>Multiplies what is behind it by its color.</summary>
        Multiply,

        /// <summary>Multiplies what is behind it by twice its color.</summary>
        Mod2x,

        /// <summary>Multiplies what is behind it by its color where it is opaque.</summary>
        ModThenAdd,
    }
}
