using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Remaps the running count of particles this initializer has seen from an input range to a
    /// configurable output range, with optional bias, wrap and invert.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapParticleCountToScalar">C_INIT_RemapParticleCountToScalar</seealso>
    class RemapParticleCountToScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly long inputMin;
        private readonly long inputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool activeRange;

        /// <summary>
        /// A key from before the set method that the format upgrade leaves in place: the remapped value
        /// scales the current one instead of going through the set method.
        /// </summary>
        private readonly bool scaleInitialRange;

        private readonly bool invert;
        private readonly bool wrap;
        private readonly float remapBias = 0.5f;

        private readonly int controlPoint = -1;
        private readonly int controlPointComponent;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        /// <summary>
        /// How many particles this initializer has been handed since the system started. This is what
        /// the input range is measured against - not the particle's own spawn ordinal. It advances for
        /// every particle in a call, and a call is skipped only when <see cref="activeRange"/> is set
        /// and the counter is already past the high end at the start of the call.
        /// </summary>
        private int count;

        public RemapParticleCountToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Long("m_nInputMin", inputMin);
            inputMax = parse.Long("m_nInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            scaleInitialRange = parse.Boolean("m_bScaleInitialRange", scaleInitialRange);
            invert = parse.Boolean("m_bInvert", invert);
            wrap = parse.Boolean("m_bWrap", wrap);
            remapBias = parse.Float("m_flRemapBias", remapBias);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            controlPoint = Math.Clamp(parse.Int32("m_nScaleControlPoint", controlPoint), -1, ParticleSystemState.ControlPointCount);
            controlPointComponent = Math.Clamp(parse.Int32("m_nScaleControlPointField", controlPointComponent), -1, 2);

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        /// <summary>
        /// The output range the count is remapped into, the authored one until a subclass replaces it.
        /// A restart goes back to the authored range.
        /// </summary>
        protected (float Min, float Max) OutputRange { get; set; }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(controlPoint, controlPointComponent);
        }

        public override void Reset()
        {
            count = 0;
            OutputRange = (outputMin, outputMax);
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override ulong WrittenFields => fieldOutput.Mask();

        /// <summary>
        /// Inverting mirrors the input range about the live particle count rather than the counter.
        /// A scale control point scales the range bounds, again leaving the counter untouched.
        /// </summary>
        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var min = inputMin;
            var max = inputMax;

            if (invert)
            {
                min = particles.Count - inputMax - 1;
                max = particles.Count - inputMin - 1;
            }

            if (controlPoint >= 0 && controlPointComponent != -1)
            {
                var scale = particleSystemState.GetControlPoint(controlPoint).Position.GetComponent(controlPointComponent);
                min = (long)(min * scale);
                max = (long)(max * scale);
            }

            var isScalar = ParticleBuffer.IsScalar(fieldOutput);
            var isInteger = fieldOutput.IsIntegerAttribute();
            var outputs = isScalar ? run.Floats(fieldOutput) : default;

            if (activeRange && count > max)
            {
                return;
            }

            for (var i = 0; i < run.Length; i++)
            {
                if (!activeRange || (count >= min && count <= max))
                {
                    var output = MathUtils.RemapValClamped(count, min, max, OutputRange.Min, OutputRange.Max);

                    // The bias shapes the output value, after the lerp, and is skipped entirely at its identity
                    if (remapBias != 0.5f)
                    {
                        output = ParticleMath.Bias(MathUtils.Saturate(output), remapBias);
                    }

                    if (isScalar)
                    {
                        var current = outputs[i];

                        output = scaleInitialRange
                            ? current * output
                            : ParticleFieldExtensions.ApplySetMethod(output, current, current, particleSystemState.CurrentStep, setMethod);

                        outputs[i] = isInteger ? BitConverter.Int32BitsToSingle((int)output) : output;
                    }
                }

                count++;

                if (wrap && count > max)
                {
                    count = 0;
                }
            }
        }
    }
}
