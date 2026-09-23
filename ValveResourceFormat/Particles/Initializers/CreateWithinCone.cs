namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Emits particles along the surface of a cone around a transform's forward axis. The full cone
    /// angle is drawn between the inner and outer angles, and the particle starts on the ring where that
    /// cone crosses the offset distance, moving outward along the cone at the given speed.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinCone">C_INIT_CreateWithinCone</seealso>
    class CreateWithinCone : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly INumberProvider innerAngle = new LiteralNumberProvider(0f);
        private readonly INumberProvider outerAngle = new LiteralNumberProvider(30f);
        private readonly INumberProvider speed = new LiteralNumberProvider(0f);
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly bool collapseOffset;

        public CreateWithinCone(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            innerAngle = parse.NumberProvider("m_flInnerAngle", innerAngle);
            outerAngle = parse.NumberProvider("m_flOuterAngle", outerAngle);
            speed = parse.NumberProvider("m_flSpeed", speed);
            offset = parse.NumberProvider("m_flOffset", offset);
            collapseOffset = parse.Boolean("m_bCollapseOffset", collapseOffset);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var inner = innerAngle.NextNumber(ref particle, particleSystemState);
            var outer = outerAngle.NextNumber(ref particle, particleSystemState);
            var particleSpeed = speed.NextNumber(ref particle, particleSystemState);
            var particleOffset = offset.NextNumber(ref particle, particleSystemState);

            var halfAngle = float.DegreesToRadians(float.Lerp(inner, outer, particleSystemState.Random.Next())) * 0.5f;

            // The ring is placed at least one unit along the axis so a zero offset still yields a
            // direction at the drawn angle; a negative offset flips the cone backwards.
            var axialDistance = MathF.Max(MathF.Abs(particleOffset), 1f) * (particleOffset < 0f ? -1f : 1f);
            var radius = axialDistance * MathF.Tan(halfAngle);

            var (sin, cos) = MathF.SinCos(particleSystemState.Random.Next() * MathF.Tau);

            var transform = transformInput.NextTransformAtTime(ref particle, particleSystemState, particle.CreationTime);
            var forward = Vector3.TransformNormal(Vector3.UnitX, transform);
            var ringOffset = Vector3.TransformNormal(new Vector3(0f, radius * cos, radius * sin), transform);

            var position = transform.Translation + ringOffset;

            if (!collapseOffset)
            {
                position += forward * particleOffset;
            }

            var direction = Vector3.Normalize((forward * axialDistance) + ringOffset);

            particle.SetVector(ParticleField.Position, position);
            particle.Velocity = direction * particleSpeed;

            return particle;
        }
    }
}
