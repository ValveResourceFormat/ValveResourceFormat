namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Scales the velocity earlier initializers gave a particle, per axis.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_ScaleVelocity">C_INIT_ScaleVelocity</seealso>
    class ScaleVelocity : ParticleFunctionInitializer
    {
        private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);

        public ScaleVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.VectorProvider("m_vecScale", scale);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var scale = this.scale.NextVector(particle, particleSystemState);

            particle.PositionPrevious = ((particle.PositionPrevious - particle.Position) * scale) + particle.Position;

            return particle;
        }
    }
}
