using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within an axis-aligned box defined by a minimum and
    /// maximum corner vector, offset by a control point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinBox">C_INIT_CreateWithinBox</seealso>
    class CreateWithinBox : ParticleFunctionInitializer
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);

        private readonly int controlPointNumber;
        private readonly RangeSampler rangeSampler;

        public CreateWithinBox(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.VectorProvider("m_vecMin", min);
            max = parse.VectorProvider("m_vecMax", max);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var posMin = min.NextVector(ref particle, particleSystemState);
            var posMax = max.NextVector(ref particle, particleSystemState);

            var position = rangeSampler.NextVectorBetween(ref particle, particleSystemState, posMin, posMax);

            var offset = particleSystemState.GetControlPoint(controlPointNumber).Position;

            particle.Position += position + offset;

            return particle;
        }
    }
}
