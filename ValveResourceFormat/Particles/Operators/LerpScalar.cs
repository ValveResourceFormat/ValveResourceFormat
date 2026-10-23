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
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var particleSpan = particles.Current;
            var inWindowCount = 0;

            foreach (var particle in particleSpan)
            {
                var time = particle.Age / (particle.Lifetime + ParticleMath.FloatEpsilon);

                if (time < startTime || time > endTime)
                {
                    continue;
                }

                var lerpTarget = output.NextNumber(particleSpan[inWindowCount++], particleSystemState);
                var initial = particle.GetInitialScalar(particles, fieldOutput);

                var scalarOutput = startTime == endTime
                    ? (time - endTime < 0f ? initial : lerpTarget)
                    : MathUtils.Lerp(initial, lerpTarget, MathUtils.Saturate((time - startTime) / (endTime - startTime)));

                particle.SetScalar(fieldOutput, MathUtils.Lerp(particle.GetScalar(fieldOutput), scalarOutput, strength));
            }
        }
    }
}
