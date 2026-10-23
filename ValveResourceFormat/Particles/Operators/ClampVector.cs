namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps each component of a vector attribute between two per-particle vector inputs.
    /// </summary>
    /// <remarks>
    /// An inverted range is not corrected: the minimum is applied first, so the maximum wins.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampVector">C_OP_ClampVector</seealso>
    class ClampVector : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);

        public ClampVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (var particle in particles.Current)
            {
                var min = outputMin.NextVector(particle, particleSystemState);
                var max = outputMax.NextVector(particle, particleSystemState);

                var current = particle.GetVector(fieldOutput);
                var clamped = Vector3.Min(max, Vector3.Max(min, current));

                particle.SetVector(fieldOutput, BlendByStrength(current, clamped, strength));
            }
        }
    }
}
