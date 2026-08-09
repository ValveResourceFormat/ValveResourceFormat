using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Drives a vector field to <c>m_vecOutput</c> over <c>m_flLerpTime</c>, starting from whatever each
    /// particle held on the operator's first endcap frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpEndCapVector">C_OP_LerpEndCapVector</seealso>
    class LerpEndCapVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly Vector3 output;
        private readonly float lerpTime;

        private float startAge = -1f;

        public LerpEndCapVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            output = parse.Vector3("m_vecOutput", output);
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
                    particle.SetInitialVector(particles, outputField, particle.GetVector(outputField));
                }
            }

            var t = (particleSystemState.Age - startAge) / (lerpTime + Epsilon.Duration);

            // The vector form stops writing once the ramp is over, where the scalar form keeps saturating,
            // and it scales the ramp position by strength rather than blending the result
            if (t > 1f)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                particle.SetVector(outputField, Vector3.Lerp(particle.GetInitialVector(particles, outputField), output, t * strength));
            }
        }
    }
}
