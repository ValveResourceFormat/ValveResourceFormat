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

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var divisor = ignoreDelta ? 1f : frameTime;
            var inputRange = inputMax == inputMin ? 1f : inputMax - inputMin;

            foreach (ref var particle in particles.Current)
            {
                var speed = (particle.Position - particle.PositionPrevious).Length() / divisor;
                var fraction = Math.Clamp((speed - inputMin) / inputRange, 0f, 1f);
                var value = (fraction * (outputMax - outputMin)) + outputMin;
                value = particle.ModifyScalarBySetMethod(particles, outputField, value, setMethod);

                var current = particle.GetScalar(outputField);
                particle.SetScalar(outputField, ((value - current) * strength) + current);
            }
        }
    }
}
