namespace GUI.Types.ParticleRenderer.Operators
{
    class Decay : IParticleOperator
    {
        public Decay()
        {
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
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
