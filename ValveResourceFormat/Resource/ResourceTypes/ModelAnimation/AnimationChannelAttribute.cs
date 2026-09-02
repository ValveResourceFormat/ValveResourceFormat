namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Specifies the type of data contained in an animation channel.
    /// Derived from the <c>m_szChannelClass</c> and <c>m_szVariableName</c> fields of
    /// <c>CAnimDataChannelDesc</c>.
    /// </summary>
    public enum AnimationChannelAttribute
    {
        /// <summary>Channel attribute is not recognized by VRF. The value an unset channel reads as.</summary>
        Unknown = 0,

        /// <summary>Channel encodes bone position (translation) data.</summary>
        Position = 1,

        /// <summary>Channel encodes bone rotation (orientation) data.</summary>
        Angle = 2,

        /// <summary>Channel encodes bone scale data.</summary>
        Scale = 3,

        /// <summary>Channel encodes flex controller (morph) data.</summary>
        Data = 4,

        /// <summary>
        /// Channel encodes a named value from the decode key's <c>m_userArray</c> (<c>m_szChannelClass</c>
        /// <c>"UserChannel"</c>), for example a <c>MATERIAL_ATTRIBUTE:</c> shader parameter driven by the
        /// sequence. VRF decodes these channels but has no consumer for the values yet.
        /// </summary>
        User = 5,
    }
}
