using System;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomYawFlip : ParticleFunctionInitializer
    {
        private readonly float percent;

        public RandomYawFlip(ParticleDefinitionParser parse) : base(parse)
        {
            percent = parse.Float("m_flPercent", percent);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (Random.Shared.NextSingle() > percent)
            {
                particle.SetScalar(ParticleField.Yaw, particle.GetScalar(ParticleField.Yaw) + MathF.PI * 0.5f);
            }

            return particle;
        }
    }
}
