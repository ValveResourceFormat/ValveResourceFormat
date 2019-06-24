using System.Collections.Generic;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class ColorInterpolate : IParticleOperator
    {
        private readonly Vector3 colorFade = Vector3.One;
        private readonly float fadeStartTime = 0f;
        private readonly float fadeEndTime = 1f;

        public ColorInterpolate(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_ColorFade"))
            {
                var vectorValues = keyValues.GetArray<long>("m_ColorFade");
                colorFade = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (keyValues.ContainsKey("m_flFadeStartTime"))
            {
                fadeStartTime = keyValues.GetFloatProperty("m_flFadeStartTime");
            }

            if (keyValues.ContainsKey("m_flFadeEndTime"))
            {
                fadeEndTime = keyValues.GetFloatProperty("m_flFadeEndTime");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var time = 1 - (particle.Lifetime / particle.ConstantLifetime);

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = (time - fadeStartTime) / (fadeEndTime - fadeStartTime);

                    // Interpolate from constant color to fade color
                    particle.Color = ((1 - t) * particle.ConstantColor) + (t * colorFade);
                }
            }
        }
    }
}
