namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Clamps a scalar particle field to a configurable minimum and maximum range each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampScalar">C_OP_ClampScalar</seealso>
    class ClampScalar : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField OutputField = ParticleField.Radius;

        public ClampScalar(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nFieldOutput", OutputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var min = outputMin.NextNumber(ref particle, particleSystemState);
                var max = outputMax.NextNumber(ref particle, particleSystemState);
                MathUtils.MinMaxFixUp(ref min, ref max);

                var clampedValue = Math.Clamp(particle.GetScalar(OutputField), min, max);
                particle.SetScalar(OutputField, clampedValue);
            }
        }
    }
}
