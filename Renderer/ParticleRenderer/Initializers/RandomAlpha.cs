using GUI.Utils;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomAlpha : ParticleFunctionInitializer
    {
        private readonly int alphaMin = 255;
        private readonly int alphaMax = 255;

        public RandomAlpha(ParticleDefinitionParser parse) : base(parse)
        {
            alphaMin = parse.Int32("m_nAlphaMin", alphaMin);
            alphaMax = parse.Int32("m_nAlphaMax", alphaMax);

            MathUtils.MinMaxFixUp(ref alphaMin, ref alphaMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var alpha = ParticleCollection.RandomBetween(particle.ParticleID, alphaMin, alphaMax) / 255f;

            particle.Alpha = alpha;

            return particle;
        }
    }
}
