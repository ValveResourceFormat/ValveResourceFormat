namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills a particle once its age reaches its lifetime.
    /// </summary>
    /// <remarks>
    /// "Lifespan Decay" in the particle editor. All effects should have a decay operator
    /// unless the particles are certain to be destroyed by some other means (usually code).
    /// With <c>m_bRopeDecay</c> a particle only dies together with the two particles after it, so a
    /// rope keeps its end segments until the whole tail has run out.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Decay">C_OP_Decay</seealso>
    class Decay : ParticleFunctionOperator
    {
        private readonly bool ropeDecay;

        public Decay(ParticleDefinitionParser parse) : base(parse)
        {
            ropeDecay = parse.Boolean("m_bRopeDecay", ropeDecay);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var particleSpan = particles.Current;

            if (!ropeDecay)
            {
                foreach (ref var particle in particleSpan)
                {
                    if (particle.Lifetime <= 0f || particle.Lifetime <= particle.Age)
                    {
                        particle.Kill();
                    }
                }

                return;
            }

            var last = particleSpan.Length - 1;

            for (var i = 0; i <= last; i++)
            {
                if (IsPastLifetime(particleSpan[i])
                    && IsPastLifetime(particleSpan[Math.Min(i + 1, last)])
                    && IsPastLifetime(particleSpan[Math.Min(i + 2, last)]))
                {
                    particleSpan[i].Kill();
                }
            }
        }

        private static bool IsPastLifetime(in Particle particle) => particle.Age > particle.Lifetime;
    }
}
