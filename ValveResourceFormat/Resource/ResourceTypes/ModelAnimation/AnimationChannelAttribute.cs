namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Specifies the type of data contained in an animation channel.
    /// Derived from the <c>m_szVariableName</c> field of <c>CAnimDataChannelDesc</c>.
    /// </summary>
    public enum AnimationChannelAttribute
    {
        /// <summary>Channel encodes bone position (translation) data.</summary>
        Position,

        /// <summary>Channel encodes bone rotation (orientation) data.</summary>
        Angle,

        /// <summary>Channel encodes bone scale data.</summary>
        Scale,

        /// <summary>Channel encodes flex controller (morph) data.</summary>
        Data,

        /// <summary>Channel attribute is not recognized by VRF.</summary>
        Unknown,
    }
}
