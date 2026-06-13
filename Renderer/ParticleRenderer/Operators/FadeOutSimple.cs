namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha field out linearly over a proportional fade-out time at the end of the particle's life.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeOutSimple">C_OP_FadeOutSimple</seealso>
    class FadeOutSimple : ParticleFunctionOperator
    {
        private readonly float fadeOutTime = 0.25f;
        private readonly ParticleField FieldOutput = ParticleField.Alpha;

        public FadeOutSimple(ParticleDefinitionParser parse) : base(parse)
        {
            fadeOutTime = parse.Float("m_flFadeOutTime", fadeOutTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var timeLeft = 1 - particle.NormalizedAge;
                if (timeLeft <= fadeOutTime)
                {
                    var t = timeLeft / fadeOutTime;
                    var newAlpha = t * particle.GetInitialScalar(particles, ParticleField.Alpha);
                    particle.SetScalar(FieldOutput, newAlpha);
                }
            }
        }
    }
}
