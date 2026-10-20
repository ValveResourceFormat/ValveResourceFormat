namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Quantizes a scalar particle attribute onto a grid, snapping it to the nearest multiple of a
    /// given step size. The rotation attributes take their step in degrees.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_QuantizeFloat">C_OP_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);
        private readonly float sizeScale = 1f;

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            quantizeSize = parse.NumberProvider("m_InputValue", quantizeSize);

            // The rotation attributes quantize in degrees; every other field is taken as authored.
            sizeScale = outputField is ParticleField.Roll or ParticleField.Yaw or ParticleField.Pitch
                ? float.DegreesToRadians(1f)
                : 1f;
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var quantizeSize = this.quantizeSize.NextNumber(ref particle, particleSystemState) * strength * sizeScale;
                var value = particle.GetScalar(outputField);

                if (quantizeSize != 0)
                {
                    value = quantizeSize * MathF.Floor((value / quantizeSize) + 0.5f);
                }

                particle.SetScalar(outputField, value);
            }
        }
    }
}
