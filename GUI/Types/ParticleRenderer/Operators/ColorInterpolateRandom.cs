using System;
using System.Numerics;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat.Serialization;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ColorInterpolateRandom : IParticleOperator
    {
        private readonly Vector3 colorFadeMin = Vector3.One;
        private readonly Vector3 colorFadeMax = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField field = ParticleField.Color;
        private readonly bool easeInOut;

        public ColorInterpolateRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_ColorFadeMin"))
            {
                var vectorValues = keyValues.GetIntegerArray("m_ColorFadeMin");
                colorFadeMin = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (keyValues.ContainsKey("m_ColorFadeMax"))
            {
                var vectorValues = keyValues.GetIntegerArray("m_ColorFadeMax");
                colorFadeMax = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
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

            if (keyValues.ContainsKey("m_bEaseInOut"))
            {
                easeInOut = keyValues.GetProperty<bool>("m_bEaseInOut");
            }
        }

        private readonly Dictionary<int, Vector3> particleColors = new();

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (!particleColors.ContainsKey(particle.ParticleCount))
                {
                    var newColor = easeInOut
                        ? MathUtils.RandomBetweenPerComponent(colorFadeMin, colorFadeMax)
                        : MathUtils.RandomBetween(colorFadeMin, colorFadeMax);

                    particleColors[particle.ParticleCount] = newColor;
                }

                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);

                    // Interpolate from constant color to fade color
                    particle.SetVector(field, MathUtils.Lerp(t, particle.InitialColor, particleColors[particle.ParticleCount]));
                }
            }
        }
    }
}
