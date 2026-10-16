using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within a box defined by a minimum and maximum corner
    /// vector, around a control point where it was at each particle's creation time, optionally in the
    /// control point's own axes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinBox">C_INIT_CreateWithinBox</seealso>
    class CreateWithinBox : ParticleFunctionInitializer
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);

        private readonly int controlPointNumber;
        private readonly bool localSpace;
        private readonly RangeSampler rangeSampler;

        public CreateWithinBox(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.VectorProvider("m_vecMin", min);
            max = parse.VectorProvider("m_vecMax", max);
            controlPointNumber = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPointNumber), 0, ParticleSystemState.MaxControlPoint);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var posMin = min.NextVector(ref particle, particleSystemState);
            var posMax = max.NextVector(ref particle, particleSystemState);

            var position = rangeSampler.NextVectorBetween(ref particle, particleSystemState, posMin, posMax);

            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            particle.Position = localSpace
                ? Vector3.Transform(position, controlPoint.GetBasisAtTime(particle.CreationTime))
                : position + controlPoint.GetPositionAtTime(particle.CreationTime);

            if (IsUnwritten(particleSystemState, ParticleField.PositionPrevious))
            {
                particle.PositionPrevious = particle.Position;
            }

            return particle;
        }
    }
}
