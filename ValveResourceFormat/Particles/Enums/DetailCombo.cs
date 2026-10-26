namespace ValveResourceFormat.Particles
{
    /// <summary>How a status effect blends its detail texture into the material.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/DetailCombo_t">DetailCombo_t</seealso>
    internal enum DetailCombo
    {
        /// <summary>No detail texture.</summary>
        DETAIL_COMBO_OFF = 0,

        /// <summary>Adds the detail texture.</summary>
        DETAIL_COMBO_ADD = 1,

        /// <summary>Adds the detail texture as self illumination.</summary>
        DETAIL_COMBO_ADD_SELF_ILLUM = 2,

        /// <summary>Multiplies by twice the detail texture.</summary>
        DETAIL_COMBO_MOD2X = 3,
    }
}
