namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps a scalar particle field to a configurable minimum and maximum range each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampScalar">C_OP_ClampScalar</seealso>
    class ClampScalar : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField outputField = ParticleField.Radius;

        public ClampScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var min = outputMin.NextNumber(ref particle, particleSystemState);
                var max = outputMax.NextNumber(ref particle, particleSystemState);

                var currentValue = particle.GetScalar(outputField);

                // An inverted range is not corrected: the engine applies the bounds in order, so
                // the maximum wins outright.
                var clampedValue = MathF.Min(max, MathF.Max(currentValue, min));

                particle.SetScalar(outputField, float.Lerp(currentValue, clampedValue, strength));
            }
        }
    }
}
