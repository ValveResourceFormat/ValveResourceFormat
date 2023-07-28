using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ColorInterpolate : IParticleOperator
    {
        private readonly Vector3 colorFade = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField field = ParticleField.Color;

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

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);

                    // Interpolate from constant color to fade color
                    particle.SetVector(field, MathUtils.Lerp(t, particle.InitialColor, colorFade));
                }
            }
        }
    }
}
