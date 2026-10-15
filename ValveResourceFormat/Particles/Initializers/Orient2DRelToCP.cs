namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets an angle attribute to the direction from a control point to the particle in the world XY
    /// plane, plus an offset in degrees. The angle is the XY heading plus pi, so a particle on the +X
    /// side of the control point reads pi. A particle directly above or below the control point is left
    /// untouched.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_Orient2DRelToCP">C_INIT_Orient2DRelToCP</seealso>
    class Orient2DRelToCP : ParticleFunctionInitializer
    {
        private readonly int cp;
        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly float rotOffset;

        public Orient2DRelToCP(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nCP", cp);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            rotOffset = float.DegreesToRadians(parse.Float("m_flRotOffset", 0f));
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (Angle(particle.Position, particleSystemState.GetControlPoint(cp).Position) is { } angle)
            {
                particle.SetScalar(fieldOutput, angle + rotOffset);
            }

            return particle;
        }

        /// <summary>
        /// The heading from <paramref name="center"/> to <paramref name="position"/> in the XY plane plus
        /// pi, in (0, 2 pi], or null when the two share an X and a Y.
        /// </summary>
        internal static float? Angle(Vector3 position, Vector3 center)
        {
            var dx = position.X - center.X;
            var dy = position.Y - center.Y;

            if (dx == 0f && dy == 0f)
            {
                return null;
            }

            return MathF.Atan2(dy, dx) + MathF.PI;
        }
    }
}
