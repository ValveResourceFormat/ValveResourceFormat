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

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var end = particleSystemState.GetControlPoint(controlPointNumberEnd);
            var offset = end.Position - particleSystemState.GetControlPoint(controlPointNumberStart).Position;

            if (localCoords)
            {
                offset = Vector3.TransformNormal(offset, end.GetRotationMatrix());
            }

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] += offset;
                previousPositions[i] += offset;
            }
        }
    }
}
