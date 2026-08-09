namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Quantizes a scalar particle attribute to a multiple of a given step size by truncating
    /// toward zero, snapping the value onto a grid.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_QuantizeFloat">C_OP_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            quantizeSize = parse.NumberProvider("m_InputValue", quantizeSize);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var quantizeSize = this.quantizeSize.NextNumber(ref particle, particleSystemState) * strength;
                var value = particle.GetScalar(outputField);

                if (quantizeSize != 0)
                {
                    value = quantizeSize * MathF.Truncate(value / quantizeSize);
                }

                particle.SetScalar(outputField, value);
            }
        }
    }
}
