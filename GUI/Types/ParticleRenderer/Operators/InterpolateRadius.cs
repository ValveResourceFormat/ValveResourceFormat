using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class InterpolateRadius : IParticleOperator
    {
        private readonly float startTime;
        private readonly float endTime = 1;
        private readonly INumberProvider startScale = new LiteralNumberProvider(1);
        private readonly INumberProvider endScale = new LiteralNumberProvider(1);
        private readonly INumberProvider bias = new LiteralNumberProvider(0);

        public InterpolateRadius(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEndTime"))
            {
                endTime = keyValues.GetFloatProperty("m_flEndTime");
            }

            if (keyValues.ContainsKey("m_flStartScale"))
            {
                startScale = keyValues.GetNumberProvider("m_flStartScale");
            }

            if (keyValues.ContainsKey("m_flEndScale"))
            {
                endScale = keyValues.GetNumberProvider("m_flEndScale");
            }

            if (keyValues.ContainsKey("m_flBias"))
            {
                bias = keyValues.GetNumberProvider("m_flBias");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                var time = 1 - (particles[i].Lifetime / particles[i].ConstantLifetime);

                if (time >= startTime && time <= endTime)
                {
                    double t = (time - startTime) / (endTime - startTime);
                    t = Math.Pow(t, 1.0 - bias.NextNumber()); // apply bias to timescale
                    var radiusScale = (startScale.NextNumber() * (1 - t)) + (endScale.NextNumber() * t);

                    particles[i].Radius = particles[i].ConstantRadius * (float)radiusScale;
                }
            }
        }
    }
}
