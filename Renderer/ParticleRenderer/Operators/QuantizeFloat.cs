using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class QuantizeFloat : ParticleFunctionOperator
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            quantizeSize = parse.NumberProvider("m_nInputValue", quantizeSize);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var quantizeSize = this.quantizeSize.NextNumber(ref particle, particleSystemState);
                var value = particle.GetScalar(OutputField);

                if (quantizeSize != 0)
                {
                    value = quantizeSize * MathF.Truncate(value / quantizeSize);
                }

                particle.SetScalar(OutputField, value);
            }
        }
    }
}
