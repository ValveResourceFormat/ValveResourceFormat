namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Draws a fresh random value for a scalar field on every particle, once, on the frame the endcap
    /// starts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ReinitializeScalarEndCap">C_OP_ReinitializeScalarEndCap</seealso>
    class ReinitializeScalarEndCap : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        private bool hasRun;

        public ReinitializeScalarEndCap(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Reset()
        {
            hasRun = false;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (hasRun || !particleSystemState.InEndCap)
            {
                return;
            }

            hasRun = true;

            foreach (ref var particle in particles.Current)
            {
                var value = particleSystemState.Random.NextBetween(outputMin, outputMax);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), value, strength));
            }
        }
    }
}
