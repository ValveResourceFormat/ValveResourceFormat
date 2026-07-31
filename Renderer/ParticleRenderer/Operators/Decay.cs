namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills a particle once its age exceeds its lifetime.
    /// </summary>
    /// <remarks>
    /// "Lifespan Decay" in the particle editor. All effects should have a decay operator
    /// unless the particles are certain to be destroyed by some other means (usually code).
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Decay">C_OP_Decay</seealso>
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
