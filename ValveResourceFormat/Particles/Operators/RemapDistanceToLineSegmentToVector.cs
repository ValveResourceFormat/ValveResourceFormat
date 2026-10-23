namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's distance to the segment or line between two control points into a
    /// vector attribute, replacing its value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDistanceToLineSegmentToVector">C_OP_RemapDistanceToLineSegmentToVector</seealso>
    class RemapDistanceToLineSegmentToVector : RemapDistanceToLineSegmentBase
    {
        private readonly ParticleField fieldOutput = ParticleField.Alpha;
        private readonly Vector3 minOutputValue;
        private readonly Vector3 maxOutputValue = Vector3.One;

        public RemapDistanceToLineSegmentToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            minOutputValue = parse.Vector3("m_vMinOutputValue", minOutputValue);
            maxOutputValue = parse.Vector3("m_vMaxOutputValue", maxOutputValue);
        }

        protected override void Write(Particle particle, float fraction)
            => particle.SetVector(fieldOutput, MathUtils.Lerp(minOutputValue, maxOutputValue, fraction));
    }
}
