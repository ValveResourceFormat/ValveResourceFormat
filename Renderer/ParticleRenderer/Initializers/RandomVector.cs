using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes a vector particle attribute to a random value with each component chosen independently between a min and max vector.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomVector">C_INIT_RandomVector</seealso>
    class RandomVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 min;
        private readonly Vector3 max;
        private readonly RangeSampler rangeSampler;

        public RandomVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            min = parse.Vector3("m_vecMin", min);
            max = parse.Vector3("m_vecMax", max);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var newVector = rangeSampler.NextVectorBetween(ref particle, particleSystemState, min, max);

            particle.SetVector(fieldOutput, newVector);

            return particle;
        }
    }
}
