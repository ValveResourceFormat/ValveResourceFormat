namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle attribute to a value provided by a float input. The target field and
    /// the input value are both configurable.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFloat">C_INIT_InitFloat</seealso>
    class InitFloat : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0);
        private readonly INumberProvider inputStrength = new LiteralNumberProvider(1f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public InitFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
            inputStrength = parse.NumberProvider("m_InputStrength", inputStrength);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var value = inputValue.NextNumber(ref particle, particleSystemState);
            value *= inputStrength.NextNumber(ref particle, particleSystemState);

            // Angles are authored in degrees and stored in radians, the same conversion the dedicated
            // rotation initializers do. The scaling set methods take a unitless multiplier, not an angle,
            // so they are left alone.
            if (outputField.IsAngleField()
                && setMethod is not (ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE))
            {
                value = float.DegreesToRadians(value);
            }

            var finalValue = particle.ModifyScalarBySetMethodAtSpawn(particles, outputField, value, setMethod);

            particle.SetScalar(outputField, finalValue);

            return particle;
        }
    }
}
