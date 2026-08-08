using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps a running count of the particles this initializer has seen to a scalar output field.
    /// The input count range is mapped to a configurable output range, with optional bias, wrap, and invert controls.
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

        /// <summary>
        /// How many particles this initializer has been handed since the system started. It advances
        /// for particles that <see cref="activeRange"/> excludes, and is what the input range is
        /// measured against - not the particle's own spawn ordinal.
        /// </summary>
        private int count;

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
            controlPoint = Math.Clamp(parse.Int32("m_nScaleControlPoint", controlPoint), -1, 64);
            controlPointComponent = Math.Clamp(parse.Int32("m_nScaleControlPointField", controlPointComponent), -1, 2);

            if (FieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Reset()
        {
            count = 0;
        }

        /// <summary>
        /// Inverting mirrors the input range about the live particle count rather than the counter.
        /// A scale control point scales the range bounds, again leaving the counter untouched.
        /// </summary>
        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var inputMin = InputMin;
            var inputMax = InputMax;

            if (invert)
            {
                inputMin = particles.Count - InputMax - 1;
                inputMax = particles.Count - InputMin - 1;
            }

            if (controlPoint >= 0 && controlPointComponent != -1)
            {
                var scale = particleSystemState.GetControlPoint(controlPoint).Position.GetComponent(controlPointComponent);
                inputMin = (long)(inputMin * scale);
                inputMax = (long)(inputMax * scale);
            }

            if (activeRange && count > inputMax)
            {
                return particle;
            }

            if (!activeRange || count >= inputMin)
            {
                // A degenerate input range is a threshold, inclusive on the high side
                var output = MathUtils.RemapValClamped(count, inputMin, inputMax, outputMin, outputMax);

                // The bias shapes the output value, after the lerp, and is skipped entirely at its identity
                if (remapBias != 0.5f)
                {
                    output = NumericBias.Standard(output, remapBias);
                }

                output = scaleInitialRange
                    ? particle.GetScalar(FieldOutput) * output
                    : particle.ModifyScalarBySetMethodAtSpawn(particles, FieldOutput, output, setMethod);

                particle.SetScalar(FieldOutput, output);
            }

            count++;

            if (wrap && count > inputMax)
            {
                count = 0;
            }

            return particle;
        }
    }
}
