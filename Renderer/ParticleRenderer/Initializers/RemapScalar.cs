namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps a scalar input particle field from one range to another and writes the result to an output field.
    /// Corresponds to <c>C_INIT_RemapScalar</c>.
    /// </summary>
    class RemapScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.Alpha;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax;
        private readonly float outputMin;
        private readonly float outputMax;

        public RemapScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var value = particle.GetScalar(fieldInput);

            value = MathUtils.RemapValClamped(value, inputMin, inputMax, outputMin, outputMax);

            particle.SetScalar(fieldOutput, value);

            return particle;
        }
    }
}
