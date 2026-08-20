using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Adds a random offset to a particle's existing normal, optionally rotated into control point local space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalOffset">C_INIT_NormalOffset</seealso>
    class NormalOffset : ParticleFunctionInitializer
    {
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.Zero;
        private readonly int controlPointNumber;
        private readonly bool localCoords;
        private readonly bool normalize;

        public NormalOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.Vector3("m_OffsetMin", offsetMin);
            offsetMax = parse.Vector3("m_OffsetMax", offsetMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.CreationTime) | FieldMask(ParticleField.Normal);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var offset = particleSystemState.Random.NextBetweenPerComponent(offsetMin, offsetMax);

            if (localCoords)
            {
                offset = ControlPointTransformProvider.TransformDirection(particleSystemState, controlPointNumber, offset);
            }

            var normal = particle.GetVector(ParticleField.Normal) + offset;

            if (normalize && normal != Vector3.Zero)
            {
                normal = ParticleMath.Normalize(normal);
            }

            particle.SetVector(ParticleField.Normal, normal);
            return particle;
        }
    }
}
