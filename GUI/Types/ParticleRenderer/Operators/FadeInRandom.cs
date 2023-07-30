using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeInRandom : IParticleOperator
    {
        private readonly float fadeInTimeMin = 0.25f;
        private readonly float fadeInTimeMax = 0.25f;
        private readonly float randomExponent = 1f;
        private readonly bool proportional = true;

        public FadeInRandom(ParticleDefinitionParser parse)
        {
            fadeInTimeMin = parse.Float("m_flFadeInTimeMin", fadeInTimeMin);
            fadeInTimeMax = parse.Float("m_flFadeInTimeMax", fadeInTimeMax);
            randomExponent = parse.Float("m_flFadeInTimeExp", randomExponent);
            proportional = parse.Boolean("m_bProportional", proportional);
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                // TODO: Consistent rng
                var fadeInTime = MathUtils.RandomWithExponentBetween(randomExponent, fadeInTimeMin, fadeInTimeMax);

                var time = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                    particle.Alpha = newAlpha;
                }
            }
        }
    }
}
