using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sends each particle from where it was created toward a control point, where it was at the
    /// particle's creation time, with a lifetime that ends on arrival. The end offset moves the target
    /// beyond the control point along the trip, and the start offset moves the particle forward along it.
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
        private readonly bool trailBias;

        public MoveBetweenPoints(ParticleDefinitionParser parse) : base(parse)
        {
            speedMin = parse.NumberProvider("m_flSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_flSpeedMax", speedMax);
            endSpread = parse.NumberProvider("m_flEndSpread", endSpread);
            startOffset = parse.NumberProvider("m_flStartOffset", startOffset);
            endOffset = parse.NumberProvider("m_flEndOffset", endOffset);
            endControlPoint = parse.Int32("m_nEndControlPointNumber", endControlPoint);
            trailBias = parse.Boolean("m_bTrailBias", trailBias);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var start = particle.Position;
            var end = particleSystemState.GetControlPoint(endControlPoint).GetPositionAtTime(particle.CreationTime);
            var spread = endSpread.NextNumber(ref particle, particleSystemState);

            if (spread > 0f)
            {
                end += particleSystemState.Random.NextInUnitBall(out _) * spread;
            }

            var delta = end - start;
            var length = delta.Length();

            var endDistance = endOffset.NextNumber(ref particle, particleSystemState);
            if (endDistance != 0f)
            {
                end += delta * (endDistance / (length + ParticleMath.FloatEpsilon));
                delta = end - start;
                length = delta.Length();
            }

            var startDistance = startOffset.NextNumber(ref particle, particleSystemState);
            if (startDistance != 0f)
            {
                start += delta * (startDistance / (length + ParticleMath.FloatEpsilon));
                delta = end - start;
                length = delta.Length();
                particle.Position = start;
            }

            var speed = particleSystemState.Random.NextBetween(
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            particle.Lifetime = (length / (speed + ParticleMath.FloatEpsilon)) + (trailBias ? particle.TrailLength : 0f);
            particle.PositionPrevious = particle.Position - (delta * (speed / (length + ParticleMath.FloatEpsilon)) * particles.PreviousFrameTime);

            return particle;
        }
    }
}
