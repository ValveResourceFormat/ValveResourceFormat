using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's current speed from an input range to an output range and writes
    /// the result to a scalar particle attribute.
    /// </summary>
    /// <remarks>
    /// Both input bounds are raised to at least 0.001. Equal bounds do not make a step: the remap
    /// becomes <c>speed - min</c> clamped to [0, 1].
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapSpeed">C_OP_RemapSpeed</seealso>
    class RemapSpeed : ParticleFunctionOperator
    {
        private const float MinimumInput = 0.001f;

        private readonly float inputMin = MinimumInput;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        /// <summary>
        /// Reads the distance the particle moved this frame instead of its speed: speed is the step
        /// divided by the frame time, and this leaves the divisor at one.
        /// </summary>
        private readonly bool ignoreDelta;

        public RemapSpeed(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            ignoreDelta = parse.Boolean("m_bIgnoreDelta", ignoreDelta);
            inputMin = MathF.Max(MinimumInput, parse.Float("m_flInputMin", inputMin));
            inputMax = MathF.Max(MinimumInput, parse.Float("m_flInputMax", inputMax));
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            if (outputField.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var divisor = ignoreDelta ? 1f : frameTime;

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);
            var output = current.Floats(outputField);
            var initial = particles.Initial.ReadFloats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                var speed = (positions[i] - previousPositions[i]).Length() / divisor;
                var fraction = ParticleMath.ClampedFraction(speed, inputMin, inputMax);
                var value = MathUtils.Lerp(outputMin, outputMax, fraction);

                value = ParticleFieldExtensions.ApplySetMethod(value, initial[i], output[i], particleSystemState.CurrentStep, setMethod);

                output[i] = MathUtils.Lerp(output[i], value, strength);
            }
        }
    }
}
