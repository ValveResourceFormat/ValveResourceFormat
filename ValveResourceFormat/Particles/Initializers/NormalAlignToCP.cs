namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the particle normal to a control point's forward direction, so ALIGN_TO_PARTICLE_NORMAL sprites
    /// face the way the control point points instead of keeping their default up-facing normal.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalAlignToCP">C_INIT_NormalAlignToCP</seealso>
    class NormalAlignToCP : ParticleFunctionInitializer
    {
        public NormalAlignToCP(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Normal);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var orientation = particleSystemState.GetControlPoint(0).Orientation;

            // The control point orientation is a forward direction; zero means unset, so keep the default normal.
            if (orientation != Vector3.Zero)
            {
                particle.Normal = Vector3.Normalize(orientation);
            }

            return particle;
        }
    }
}
