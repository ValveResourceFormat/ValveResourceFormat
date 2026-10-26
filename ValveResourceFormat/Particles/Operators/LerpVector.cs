using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lerps a vector attribute toward a target vector over a specified window of normalized lifetime.
    /// Particles outside the window are left as they are.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpVector">C_OP_LerpVector</seealso>
    class LerpVector : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 output = Vector3.Zero;
        private readonly float startTime;
        private readonly float endTime = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public LerpVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            output = parse.Vector3("m_vecOutput", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            AddInitialReadOf(parse, setMethod, fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var values = current.Vectors(fieldOutput);
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var readsInitial = setMethod.ReadsInitialValue();
            var initialValues = readsInitial ? particles.Initial.ReadVectors(fieldOutput) : default;

            var ages = current.ReadAges();

            for (var i = 0; i < values.Length; i++)
            {
                var time = ages[i] / (lifetimes[i] + ParticleMath.FloatEpsilon);

                if (time < startTime || time > endTime)
                {
                    continue;
                }

                float weight;

                if (startTime == endTime)
                {
                    weight = time - endTime < 0f ? 0f : 1f;
                }
                else
                {
                    weight = MathUtils.Saturate((time - startTime) / (endTime - startTime));
                }

                // The set method affects the value the vector is interpolating to, instead of the current interpolated value.
                var initialValue = readsInitial ? initialValues[i] : Vector3.Zero;
                var lerpTarget = ParticleFieldExtensions.ApplySetMethod(output, initialValue, values[i], particleSystemState.CurrentStep, setMethod);

                // The lerp runs from the spawn initial only for the two initial-value set methods
                var lerpBase = readsInitial ? initialValue : values[i];

                values[i] = MathUtils.Lerp(lerpBase, lerpTarget, weight * strength);
            }
        }
    }
}
