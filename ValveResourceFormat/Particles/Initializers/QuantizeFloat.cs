namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Rounds a float attribute of each new particle to the nearest multiple of a per-particle step,
    /// taken in degrees for an angle. A step of 0 leaves the attribute alone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_QuantizeFloat">C_INIT_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionInitializer
    {
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0f);
        private readonly ParticleField outputField = ParticleField.Radius;

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
            outputField = parse.ParticleField("m_nOutputField", outputField);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var step = inputValue.NextNumber(particle, particleSystemState);

            if (outputField.IsOrientationField())
            {
                step = float.DegreesToRadians(step);
            }

            if (step != 0f)
            {
                particle.SetScalar(outputField, MathF.Floor((particle.GetScalar(outputField) / step) + 0.5f) * step);
            }

            return particle;
        }
    }
}
