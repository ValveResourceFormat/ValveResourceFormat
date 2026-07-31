namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a configurable time window and then fades it out, killing the particle at the end of the fade-out.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade and Decay" in the particle editor: essentially combines the operators
    /// "Lifespan Decay", "Alpha Fade In Simple" and "Alpha Fade Out Simple".
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeAndKill">C_OP_FadeAndKill</seealso>
    class FadeAndKill : ParticleFunctionOperator
    {
        private readonly float startFadeInTime;
        private readonly float endFadeInTime = 0.5f;
        private readonly float startFadeOutTime = 0.5f;
        private readonly float endFadeOutTime = 1f;

        private readonly float startAlpha = 1f;
        private readonly float endAlpha;

        public FadeAndKill(ParticleDefinitionParser parse) : base(parse)
        {
            startFadeInTime = parse.Float("m_flStartFadeInTime", startFadeInTime);
            endFadeInTime = parse.Float("m_flEndFadeInTime", endFadeInTime);
            startFadeOutTime = parse.Float("m_flStartFadeOutTime", startFadeOutTime);
            endFadeOutTime = parse.Float("m_flEndFadeOutTime", endFadeOutTime);
            startAlpha = parse.Float("m_flStartAlpha", startAlpha);
            endAlpha = parse.Float("m_flEndAlpha", endAlpha);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;
                var initialAlpha = particle.GetInitialScalar(particles, ParticleField.Alpha);

                // If fading in
                if (time >= startFadeInTime && time <= endFadeInTime)
                {
                    var blend = MathUtils.Remap(time, startFadeInTime, endFadeInTime);

                    // Interpolate from initialAlpha * startAlpha up to initialAlpha
                    particle.Alpha = float.Lerp(initialAlpha * startAlpha, initialAlpha, blend);
                }

                // If fading out
                if (time >= startFadeOutTime && time <= endFadeOutTime)
                {
                    var blend = MathUtils.Remap(time, startFadeOutTime, endFadeOutTime);

                    // Interpolate from initialAlpha down to initialAlpha * endAlpha
                    particle.Alpha = float.Lerp(initialAlpha, initialAlpha * endAlpha, blend);
                }

                if (time >= endFadeOutTime)
                {
                    particle.Kill();
                }
            }
        }
    }
}
