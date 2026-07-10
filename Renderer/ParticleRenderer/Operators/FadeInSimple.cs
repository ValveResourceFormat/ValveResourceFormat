namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha field in linearly over a proportional fade-in time at the start of the particle's life.
    /// </summary>
    /// <remarks>
    /// The fade-in time is a fraction of the particle's lifetime in the 0–1 range: a setting of
    /// 0.5 on a particle with a 4-second lifetime takes 2 seconds to fade in completely.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeInSimple">C_OP_FadeInSimple</seealso>
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
