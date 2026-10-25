using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Drives a vector field to <c>m_vecOutput</c> over <c>m_flLerpTime</c>, starting from whatever each
    /// particle held on the operator's first endcap frame.
    /// </summary>
    /// <remarks>
    /// Only the X component is captured at the start of the endcap; Y and Z lerp from the particle's
    /// spawn value. The run strength compounds along the particle list, so particle <c>i</c> lerps by
    /// <c>t * strength^(i + 1)</c>.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpEndCapVector">C_OP_LerpEndCapVector</seealso>
    class LerpEndCapVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 output;
        private readonly float lerpTime = 1f;

        private float startAge = -1f;

        public LerpEndCapVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            output = parse.Vector3("m_vecOutput", output);
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

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var values = particles.Current.Vectors(outputField);
            var initialValues = particles.Initial.Vectors(outputField);

            if (startAge < 0f)
            {
                startAge = particleSystemState.Age;

                for (var i = 0; i < values.Length; i++)
                {
                    initialValues[i].X = values[i].X;
                }
            }

            var t = (particleSystemState.Age - startAge) / (lerpTime + ParticleMath.FloatEpsilon);

            // Unlike the scalar form, this stops after the ramp and applies strength to the ramp, not the result.
            if (t > 1f)
            {
                return;
            }

            for (var i = 0; i < values.Length; i++)
            {
                t *= strength;
                values[i] = MathUtils.Lerp(initialValues[i], output, t);
            }
        }
    }
}
