using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RemapParticleCountToScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly long InputMin;
        private readonly long InputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
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
            scaleInitialRange = parse.Boolean("m_bScaleInitialRange", scaleInitialRange);
            invert = parse.Boolean("m_bInvert", invert);
            wrap = parse.Boolean("m_bWrap", wrap);
            remapBias = parse.Float("m_flRemapBias", remapBias);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            controlPoint = parse.Int32("m_nScaleControlPoint", controlPoint);
            controlPointComponent = parse.Int32("m_nScaleControlPointField", controlPointComponent);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            // system state currently doesn't track total count, so we can't access that yet
            var count = invert
                ? particleSystemState.ParticleCount - particle.ParticleID
                : particle.ParticleID;

            var remappedRange = MathUtils.Remap(count, InputMin, InputMax);

            remappedRange = NumericBias.ApplyBias(remappedRange, remapBias);

            if (controlPoint != -1)
            {
                var cp = particleSystemState.GetControlPoint(controlPoint);
                remappedRange *= cp.Position.GetComponent(controlPointComponent);
            }

            remappedRange = wrap
                ? MathUtils.Wrap(remappedRange, 0f, 1f)
                : MathUtils.Saturate(remappedRange);

            var output = float.Lerp(outputMin, outputMax, remappedRange);

            if (scaleInitialRange || setMethod == ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE)
            {
                output = particle.GetScalar(FieldOutput) * output;
            }
            else if (setMethod == ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE)
            {
                output = particle.GetScalar(FieldOutput) + output;
            }

            particle.SetScalar(FieldOutput, output);

            // Why are we returning an object that we already use a ref for?
            return particle;
        }
    }
}
