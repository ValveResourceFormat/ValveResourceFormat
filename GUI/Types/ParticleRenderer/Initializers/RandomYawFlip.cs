using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomYawFlip : IParticleInitializer
    {
        private readonly float percent;

        public RandomYawFlip(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flPercent"))
            {
                percent = keyValues.GetFloatProperty("m_flPercent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (Random.Shared.NextSingle() > percent)
            {
                particle.SetInitialScalar(ParticleField.Yaw, particle.GetInitialScalar(ParticleField.Yaw) + MathF.PI * 0.5f);
            }

            return particle;
        }
    }
}
