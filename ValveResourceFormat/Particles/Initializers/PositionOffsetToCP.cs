namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Shifts each particle's spawn position and previous position by the vector from one control
    /// point to another, as both stand now rather than at the particle's creation time. With local
    /// coordinates the vector is rotated by the end control point's orientation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionOffsetToCP">C_INIT_PositionOffsetToCP</seealso>
    class PositionOffsetToCP : ParticleFunctionInitializer
    {
        private readonly int controlPointNumberStart;
        private readonly int controlPointNumberEnd = 1;
        private readonly bool localCoords;

        public PositionOffsetToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPointNumberStart = parse.ControlPoint("m_nControlPointNumberStart", controlPointNumberStart);
            controlPointNumberEnd = parse.ControlPoint("m_nControlPointNumberEnd", controlPointNumberEnd);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var end = particleSystemState.GetControlPoint(controlPointNumberEnd);
            var offset = end.Position - particleSystemState.GetControlPoint(controlPointNumberStart).Position;

            if (localCoords)
            {
                offset = Vector3.TransformNormal(offset, end.GetRotationMatrix());
            }

            particle.Position += offset;
            particle.PositionPrevious += offset;

            return particle;
        }
    }
}
