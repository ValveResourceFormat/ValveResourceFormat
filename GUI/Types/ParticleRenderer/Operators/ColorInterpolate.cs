using System;
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
                var vectorValues = keyValues.GetIntegerArray("m_ColorFade");
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

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (int i = 0; i < particles.Length; ++i)
            {
                var time = 1 - (particles[i].Lifetime / particles[i].ConstantLifetime);

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = (time - fadeStartTime) / (fadeEndTime - fadeStartTime);

                    // Interpolate from constant color to fade color
                    particles[i].Color = ((1 - t) * particles[i].ConstantColor) + (t * colorFade);
                }
            }
        }
    }
}
