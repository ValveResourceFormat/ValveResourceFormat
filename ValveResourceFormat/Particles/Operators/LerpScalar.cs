using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lerps a scalar attribute from its initial value toward a target over a normalized lifetime window.
    /// Particles outside the window are left as they are.
    /// </summary>
    /// <remarks>
    /// The target is evaluated in particle order but handed out only to particles inside the window,
    /// so a per-particle target reaches the k-th particle in the window as the value of particle k.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpScalar">C_OP_LerpScalar</seealso>
    class LerpScalar : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider output = new LiteralNumberProvider(1);
        private readonly float startTime;
        private readonly float endTime = 1f;

        public LerpScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            output = parse.NumberProvider("m_flOutput", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);

            parse.AddInitialRead(fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var values = current.Floats(fieldOutput);
            var initialValues = particles.Initial.ReadFloats(fieldOutput);
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);

            var ages = current.ReadAges();

            var targets = output.Fill(current, particleSystemState);

            // The input is consumed in order of the particles inside the window, not by particle
            var inWindowCount = 0;

            for (var i = 0; i < values.Length; i++)
            {
                var time = ages[i] / (lifetimes[i] + ParticleMath.FloatEpsilon);

                if (!(time >= startTime && time <= endTime))
                {
                    continue;
                }

                var lerpTarget = targets[inWindowCount++];
                var initial = initialValues[i];

                float scalarOutput;

                if (startTime == endTime)
                {
                    scalarOutput = time - endTime < 0f ? initial : lerpTarget;
                }
                else
                {
                    scalarOutput = MathUtils.Lerp(initial, lerpTarget, MathUtils.Saturate((time - startTime) / (endTime - startTime)));
                }

                values[i] = MathUtils.Lerp(values[i], scalarOutput, strength);
            }
        }
    }
}
