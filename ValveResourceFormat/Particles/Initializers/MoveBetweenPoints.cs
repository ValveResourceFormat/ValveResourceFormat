namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sends each particle from where it was created toward a control point, with a lifetime that ends on arrival.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_MoveBetweenPoints">C_INIT_MoveBetweenPoints</seealso>
    class MoveBetweenPoints : ParticleFunctionInitializer
    {
        private readonly INumberProvider speedMin = new LiteralNumberProvider(1f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(1f);
        private readonly INumberProvider endSpread = new LiteralNumberProvider(0f);
        private readonly INumberProvider startOffset = new LiteralNumberProvider(0f);
        private readonly INumberProvider endOffset = new LiteralNumberProvider(0f);
        private readonly int endControlPoint = 1;

        public MoveBetweenPoints(ParticleDefinitionParser parse) : base(parse)
        {
            speedMin = parse.NumberProvider("m_flSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_flSpeedMax", speedMax);
            endSpread = parse.NumberProvider("m_flEndSpread", endSpread);
            startOffset = parse.NumberProvider("m_flStartOffset", startOffset);
            endOffset = parse.NumberProvider("m_flEndOffset", endOffset);
            endControlPoint = parse.Int32("m_nEndControlPointNumber", endControlPoint);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var end = particleSystemState.GetControlPoint(endControlPoint).Position;
            var spread = endSpread.NextNumber(ref particle, particleSystemState);

            if (spread > 0f)
            {
                end += particleSystemState.Random.NextInUnitBall(out _) * spread;
            }

            var delta = end - particle.Position;
            var length = delta.Length();
            var speed = particleSystemState.Random.NextBetween(
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            if (length <= 0f || speed <= 0f)
            {
                return particle;
            }

            var direction = delta / length;
            var offset = MathF.Min(length, startOffset.NextNumber(ref particle, particleSystemState));

            particle.Position += direction * offset;
            length = MathF.Max(0f, length - offset - endOffset.NextNumber(ref particle, particleSystemState));

            particle.Lifetime = length / speed;
            particle.Velocity = direction * speed;

            return particle;
        }
    }
}
