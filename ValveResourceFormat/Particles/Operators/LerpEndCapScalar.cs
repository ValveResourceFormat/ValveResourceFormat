using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Drives a scalar field to <c>m_flOutput</c> over <c>m_flLerpTime</c>, starting from whatever each
    /// particle held on the operator's first endcap frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpEndCapScalar">C_OP_LerpEndCapScalar</seealso>
    class LerpEndCapScalar : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float output = 1f;
        private readonly float lerpTime = 1f;

        private float startAge = -1f;

        public LerpEndCapScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            output = parse.Float("m_flOutput", output);
            lerpTime = parse.Float("m_flLerpTime", lerpTime);

            parse.AddInitialRead(outputField);
        }

        public override void Reset()
        {
            startAge = -1f;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        /// <inheritdoc/>
        public override ulong InitialWrittenFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var values = particles.Current.Floats(outputField);
            var initialValues = particles.Initial.Floats(outputField);

            if (startAge < 0f)
            {
                startAge = particleSystemState.Age;
                values.CopyTo(initialValues);
            }

            var t = MathUtils.Saturate((particleSystemState.Age - startAge) / (lerpTime + ParticleMath.FloatEpsilon));

            for (var i = 0; i < values.Length; i++)
            {
                var target = MathUtils.Lerp(initialValues[i], output, t);

                values[i] = MathUtils.Lerp(values[i], target, strength);
            }
        }
    }
}
