using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ColorInterpolate : ParticleFunctionOperator
    {
        private readonly Vector3 colorFade = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField FieldOutput = ParticleField.Color;

        public ColorInterpolate(ParticleDefinitionParser parse) : base(parse)
        {
            colorFade = parse.Color24("m_ColorFade", colorFade);
            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);

                    // Interpolate from constant color to fade color
                    particle.SetVector(FieldOutput, Vector3.Lerp(particle.GetInitialVector(particles, ParticleField.Color), colorFade, t));
                }
            }
        }
    }
}
