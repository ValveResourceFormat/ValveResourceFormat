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
        }

        public override void Reset()
        {
            startAge = -1f;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            if (startAge < 0f)
            {
                startAge = particleSystemState.Age;

                foreach (ref var particle in particles.Current)
                {
                    particle.SetInitialScalar(particles, outputField, particle.GetScalar(outputField));
                }
            }

            var t = MathUtils.Saturate((particleSystemState.Age - startAge) / (lerpTime + ParticleMath.FloatEpsilon));

            foreach (ref var particle in particles.Current)
            {
                var target = float.Lerp(particle.GetInitialScalar(particles, outputField), output, t);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), target, strength));
            }
        }
    }
}
