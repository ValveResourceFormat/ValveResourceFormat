using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeInSimple : ParticleFunctionOperator
    {
        private readonly float fadeInTime = 0.25f;
        private readonly ParticleField FieldOutput = ParticleField.Alpha;

        public FadeInSimple(ParticleDefinitionParser parse) : base(parse)
        {
            fadeInTime = parse.Float("m_flFadeInTime", fadeInTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;
                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                    particle.SetScalar(FieldOutput, newAlpha);
                }
            }
        }
    }
}
