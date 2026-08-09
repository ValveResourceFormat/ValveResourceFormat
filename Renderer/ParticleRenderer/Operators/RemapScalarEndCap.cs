namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps one scalar field into another, once, on the frame the endcap starts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapScalarEndCap">C_OP_RemapScalarEndCap</seealso>
    class RemapScalarEndCap : ParticleFunctionOperator
    {
        private readonly ParticleField inputField = ParticleField.Radius;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        private bool hasRun;

        public RemapScalarEndCap(ParticleDefinitionParser parse) : base(parse)
        {
            inputField = parse.ParticleField("m_nFieldInput", inputField);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
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
                // A degenerate input range is a threshold, inclusive on the high side
                var remapped = MathUtils.RemapValClamped(particle.GetScalar(inputField), inputMin, inputMax, outputMin, outputMax);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), remapped, strength));
            }
        }
    }
}
