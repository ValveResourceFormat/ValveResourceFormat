using ValveResourceFormat.Particles.Utils;

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

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var positions = run.ReadVectors(ParticleField.Position);
            var outputs = run.Floats(fieldOutput);
            var center = particleSystemState.GetControlPoint(cp).Position;

            for (var i = 0; i < outputs.Length; i++)
            {
                if (Angle(positions[i], center) is { } angle)
                {
                    outputs[i] = angle + rotOffset;
                }
            }
        }

        /// <summary>
        /// The heading from <paramref name="center"/> to <paramref name="position"/> in the XY plane plus
        /// pi, in (0, 2 pi], or null when the two share an X and a Y.
        /// </summary>
        internal static float? Angle(Vector3 position, Vector3 center)
        {
            var direction = position - center;

            if (direction.X == 0f && direction.Y == 0f)
            {
                return null;
            }

            return ParticleMath.Heading(direction);
        }
    }
}
