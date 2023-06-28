using System;
using GUI.Utils;
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
                var time = particles[i].NormalizedAge;

                if (time >= startTime && time <= endTime)
                {
                    var startScale = this.startScale.NextNumber(particles[i], particleSystemState);
                    var endScale = this.endScale.NextNumber(particles[i], particleSystemState);

                    var timeScale = MathUtils.Remap(time, startTime, endTime);
                    timeScale = MathF.Pow(timeScale, 1.0f - bias.NextNumber(particles[i], particleSystemState)); // apply bias to timescale
                    var radiusScale = MathUtils.Lerp(timeScale, startScale, endScale);

                    particles[i].Radius = particles[i].InitialRadius * radiusScale;
                }
            }
        }
    }
}
