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
        private readonly ParticleField FieldOutput = ParticleField.Color;
        private readonly bool easeInOut;

        public ColorInterpolateRandom(ParticleDefinitionParser parse)
        {
            if (parse.Data.ContainsKey("m_ColorFadeMin"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorFadeMin");
                colorFadeMin = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (parse.Data.ContainsKey("m_ColorFadeMax"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorFadeMax");
                colorFadeMax = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            easeInOut = parse.Boolean("m_bEaseInOut", easeInOut);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // TODO: Consistent rng
                var newColor = easeInOut
                    ? MathUtils.RandomBetweenPerComponent(colorFadeMin, colorFadeMax)
                    : MathUtils.RandomBetween(colorFadeMin, colorFadeMax);

                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);

                    // Interpolate from constant color to fade color
                    particle.SetVector(FieldOutput, MathUtils.Lerp(t, particle.InitialColor, newColor));
                }
            }
        }
    }
}
