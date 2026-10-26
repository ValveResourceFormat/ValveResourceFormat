namespace ValveResourceFormat.Particles
{
    /// <summary>Where a <see cref="ParticleBuffer"/> keeps a <see cref="ParticleField"/>, from <see cref="ParticleBuffer.StorageOf"/>.</summary>
    internal enum ParticleFieldStorage
    {
        /// <summary>Not a value attribute: a handle field, or no field at all.</summary>
        None,

        /// <summary><see cref="ParticleField.BoneIndices"/>, 8 bytes per particle.</summary>
        BoneIndices,

        /// <summary>A vector attribute, in <see cref="ParticleBuffer.Vectors"/>.</summary>
        Vector,

        /// <summary>An integer attribute, stored as its bits in <see cref="ParticleBuffer.Scalars"/>.</summary>
        Integer,

        /// <summary>A float attribute, in <see cref="ParticleBuffer.Scalars"/>.</summary>
        Scalar,
    }
}
