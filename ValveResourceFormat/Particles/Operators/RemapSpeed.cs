namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's current speed from an input range to an output range and writes
    /// the result to a scalar particle attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapSpeed">C_OP_RemapSpeed</seealso>
    class RemapSpeed : ParticleFunctionOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        /// <summary>
        /// Reads the distance the particle moved this frame instead of its speed: the engine divides
        /// the step by the frame time, and this leaves the divisor at one.
        /// </summary>
        private readonly bool ignoreDelta;

        public RemapSpeed(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            ignoreDelta = parse.Boolean("m_bIgnoreDelta", ignoreDelta);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);
                var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                var speed = ignoreDelta ? particle.Speed * frameTime : particle.Speed;
                var finalValue = MathUtils.RemapValClamped(speed, inputMin, inputMax, outputMin, outputMax);
                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), finalValue, strength));
            }
        }
    }
}
