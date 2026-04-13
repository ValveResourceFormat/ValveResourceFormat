namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Quantizes a scalar particle attribute to the nearest multiple of a given step size,
    /// effectively snapping the value to a grid.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_QuantizeFloat">C_OP_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionOperator
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            quantizeSize = parse.NumberProvider("m_InputValue", quantizeSize);
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
