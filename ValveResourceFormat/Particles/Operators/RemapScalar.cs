namespace ValveResourceFormat.Particles.Operators
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

        /// <summary>Selects the retired remap, which clamps the ratio instead of the input.</summary>
        private readonly bool oldCode;

        public RemapScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            oldCode = parse.Boolean("m_bOldCode", oldCode);

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var input = particle.GetScalar(fieldInput);
                var current = particle.GetScalar(fieldOutput);

                if (oldCode)
                {
                    var output = inputMin == inputMax
                        ? (input - inputMax < 0f ? outputMin : outputMax)
                        : MathUtils.RemapValClamped(input, inputMin, inputMax, outputMin, outputMax);

                    particle.SetScalar(fieldOutput, MathUtils.Lerp(current, output, strength));
                    continue;
                }

                // The current path clamps the input into the range and then maps it, rather than
                // clamping the ratio, which differs once the input range is inverted. A degenerate
                // range therefore always reads as the top of the range.
                var clamped = MathF.Max(inputMin, MathF.Min(inputMax, input));
                var remapped = inputMin == inputMax
                    ? (float.IsNaN(clamped) ? outputMin : outputMax)
                    : outputMin + ((outputMax - outputMin) * (clamped - inputMin) / (inputMax - inputMin));

                particle.SetScalar(fieldOutput, BlendByStrength(current, remapped, strength));
            }
        }
    }
}
