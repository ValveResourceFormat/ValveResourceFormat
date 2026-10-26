namespace ValveResourceFormat.Particles
{
    /// <summary>How a status effect blends its second detail texture into the material.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/Detail2Combo_t">Detail2Combo_t</seealso>
    internal enum Detail2Combo
    {
        /// <summary>Leaves the material's own setting alone.</summary>
        DETAIL_2_COMBO_UNINITIALIZED = -1,

        /// <summary>No second detail texture.</summary>
        DETAIL_2_COMBO_OFF = 0,

        /// <summary>Adds the detail texture.</summary>
        DETAIL_2_COMBO_ADD = 1,

        /// <summary>Adds the detail texture as self illumination.</summary>
        DETAIL_2_COMBO_ADD_SELF_ILLUM = 2,

        /// <summary>Multiplies by twice the detail texture.</summary>
        DETAIL_2_COMBO_MOD2X = 3,

        /// <summary>Multiplies by the detail texture.</summary>
        DETAIL_2_COMBO_MUL = 4,

        /// <summary>Crossfades to the detail texture.</summary>
        DETAIL_2_COMBO_CROSSFADE = 5,
    }
}
