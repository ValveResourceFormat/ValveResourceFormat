namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's distance to the segment or line between two control points into a
    /// scalar attribute, replacing its value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDistanceToLineSegmentToScalar">C_OP_RemapDistanceToLineSegmentToScalar</seealso>
    class RemapDistanceToLineSegmentToScalar : RemapDistanceToLineSegmentBase
    {
        private readonly ParticleField fieldOutput = ParticleField.Alpha;
        private readonly float minOutputValue;
        private readonly float maxOutputValue = 1f;

        public RemapDistanceToLineSegmentToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            minOutputValue = parse.Float("m_flMinOutputValue", minOutputValue);
            maxOutputValue = parse.Float("m_flMaxOutputValue", maxOutputValue);
        }

        protected override void Write(Particle particle, float fraction)
            => particle.SetScalar(fieldOutput, MathUtils.Lerp(minOutputValue, maxOutputValue, fraction));
    }
}
