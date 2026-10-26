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
            scale = parse.CollectionVectorProvider("m_vecScale", scale);
        }

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var scaleValue = scale.NextVector(particleSystemState);

            var positions = run.ReadVectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < previousPositions.Length; i++)
            {
                previousPositions[i] = ((previousPositions[i] - positions[i]) * scaleValue) + positions[i];
            }
        }
    }
}
