using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeOutRandom : ParticleFunctionOperator
    {
        private readonly float fadeOutTimeMin = 0.25f;
        private readonly float fadeOutTimeMax = 0.25f;
        private readonly float fadeBias = 0.5f;
        private readonly float randomExponent = 1f;
        private readonly bool proportional = true;

        public FadeOutRandom(ParticleDefinitionParser parse) : base(parse)
        {
            fadeOutTimeMin = parse.Float("m_flFadeOutTimeMin", fadeOutTimeMin);
            fadeOutTimeMax = parse.Float("m_flFadeOutTimeMax", fadeOutTimeMax);
            randomExponent = parse.Float("m_flFadeOutTimeExp", randomExponent);
            proportional = parse.Boolean("m_bProportional", proportional);

            var bias = parse.Float("m_flFadeBias", fadeBias);

            if (bias == 0.0f)
            {
                bias = 0.5f;
            }

            fadeBias = bias;

            // Other things that exist that don't seem to do anything:
            // m_bEaseInAndOut
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var fadeOutTime = fadeOutTimeMin;

                if (fadeOutTimeMin != fadeOutTimeMax)
                {
                    fadeOutTime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, randomExponent, fadeOutTimeMin, fadeOutTimeMax);
                }

                var timeLeft = proportional
                    ? 1.0f - particle.NormalizedAge
                    : particle.Lifetime - particle.Age;

                if (timeLeft <= fadeOutTime)
                {
                    var newAlpha = (timeLeft / fadeOutTime) * particle.GetInitialScalar(particles, ParticleField.Alpha);

                    if (fadeBias != 0.5f)
                    {
                        newAlpha /= ((1f / fadeBias - 2f) * (1 - newAlpha) + 1f);
                    }

                    particle.Alpha = newAlpha;
                }
            }
        }
    }
}
