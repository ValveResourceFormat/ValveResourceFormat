namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Randomly flips the yaw of a particle by 180 degrees based on a configurable flip percentage.
    /// Corresponds to <c>C_INIT_RandomYawFlip</c>.
    /// </summary>
    class RandomYawFlip : ParticleFunctionInitializer
    {
        private readonly float percent;

        public RandomYawFlip(ParticleDefinitionParser parse) : base(parse)
        {
            percent = parse.Float("m_flPercent", 0.5f);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (Random.Shared.NextSingle() > percent)
            {
                particle.SetScalar(ParticleField.Yaw, particle.GetScalar(ParticleField.Yaw) + MathF.PI);
            }

            return particle;
        }
    }
}
