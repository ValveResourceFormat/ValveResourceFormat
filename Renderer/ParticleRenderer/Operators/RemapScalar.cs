namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps a scalar particle attribute from an input range to an output range every frame,
    /// blending toward the remapped value by operator strength. A degenerate input range acts as a
    /// step function, and the output bounds clamp to 0-1 when writing an alpha field.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapScalar">C_OP_RemapScalar</seealso>
    class RemapScalar : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Alpha;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var blend = MathF.Min(strength, 1f);

            foreach (ref var particle in particles.Current)
            {
                var input = particle.GetScalar(fieldInput);

                var output = inputMin == inputMax
                    ? (input >= inputMax ? outputMax : outputMin)
                    : MathUtils.RemapValClamped(input, inputMin, inputMax, outputMin, outputMax);

                particle.SetScalar(fieldOutput, float.Lerp(particle.GetScalar(fieldOutput), output, blend));
            }
        }
    }
}
