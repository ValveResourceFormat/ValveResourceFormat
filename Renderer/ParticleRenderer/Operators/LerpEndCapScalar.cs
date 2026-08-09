using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Drives a scalar field to <c>m_flOutput</c> over <c>m_flLerpTime</c>, starting from whatever each
    /// particle held on the operator's first endcap frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpEndCapScalar">C_OP_LerpEndCapScalar</seealso>
    class LerpEndCapScalar : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float output;
        private readonly float lerpTime;

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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
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

            var t = MathUtils.Saturate((particleSystemState.Age - startAge) / (lerpTime + Epsilon.Duration));

            foreach (ref var particle in particles.Current)
            {
                var target = float.Lerp(particle.GetInitialScalar(particles, outputField), output, t);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), target, strength));
            }
        }
    }
}
