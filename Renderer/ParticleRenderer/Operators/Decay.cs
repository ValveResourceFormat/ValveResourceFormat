namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills a particle once its age exceeds its lifetime.
    /// </summary>
    class Decay : ParticleFunctionOperator
    {
        public Decay(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age > particle.Lifetime)
                {
                    particle.Kill();
                }
            }
        }
    }
}
