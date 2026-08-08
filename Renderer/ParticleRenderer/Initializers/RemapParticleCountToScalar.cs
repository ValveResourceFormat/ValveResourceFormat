using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps a particle's index within the system (or the inverse) to a scalar output field.
    /// The input particle count range is mapped to a configurable output range, with optional bias, wrap, and invert controls.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapParticleCountToScalar">C_INIT_RemapParticleCountToScalar</seealso>
    class RemapParticleCountToScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly long InputMin;
        private readonly long InputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool activeRange;
        private readonly bool scaleInitialRange; // legacy

        private readonly bool invert;
        private readonly bool wrap;
        private readonly float remapBias = 0.5f;

        private readonly int controlPoint = -1;
        private readonly int controlPointComponent;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public RemapParticleCountToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            InputMin = parse.Long("m_nInputMin", InputMin);
            InputMax = parse.Long("m_nInputMax", InputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            scaleInitialRange = parse.Boolean("m_bScaleInitialRange", scaleInitialRange);
            invert = parse.Boolean("m_bInvert", invert);
            wrap = parse.Boolean("m_bWrap", wrap);
            remapBias = parse.Float("m_flRemapBias", remapBias);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            controlPoint = parse.Int32("m_nScaleControlPoint", controlPoint);
            controlPointComponent = parse.Int32("m_nScaleControlPointField", controlPointComponent);

            if (FieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var count = invert
                ? particleSystemState.ParticleCount - particle.CreationIndex
                : particle.CreationIndex;

            if (activeRange && (count < InputMin || count > InputMax))
            {
                return particle;
            }

            // A degenerate input range is a threshold, inclusive on the high side
            var remappedRange = InputMin == InputMax
                ? (count >= InputMax ? 1f : 0f)
                : MathUtils.Remap(count, InputMin, InputMax);

            if (controlPoint != -1)
            {
                var cp = particleSystemState.GetControlPoint(controlPoint);
                remappedRange *= cp.Position.GetComponent(controlPointComponent);
            }

            remappedRange = wrap
                ? MathUtils.Wrap(remappedRange, 0f, 1f)
                : MathUtils.Saturate(remappedRange);

            var output = float.Lerp(outputMin, outputMax, remappedRange);

            // The bias shapes the output value, after the lerp, and is skipped entirely at its identity
            if (remapBias != 0.5f)
            {
                output = NumericBias.Standard(output, remapBias);
            }

            if (scaleInitialRange || setMethod == ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE)
            {
                output = particle.GetScalar(FieldOutput) * output;
            }
            else if (setMethod == ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE)
            {
                output = particle.GetScalar(FieldOutput) + output;
            }

            particle.SetScalar(FieldOutput, output);

            return particle;
        }
    }
}
