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

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var endSpreads = endSpread.Fill(run, particleSystemState);
            var endOffsets = endOffset.Fill(run, particleSystemState);
            var startOffsets = startOffset.Fill(run, particleSystemState);
            var speedMins = speedMin.Fill(run, particleSystemState);
            var speedMaxs = speedMax.Fill(run, particleSystemState);

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var trailLengths = trailBias ? run.ReadFloats(ParticleField.TrailLength) : default;
            var controlPoint = particleSystemState.GetControlPoint(endControlPoint);

            for (var i = 0; i < positions.Length; i++)
            {
                var start = positions[i];
                var end = controlPoint.GetPositionAtTime(creationTimes[i]);
                var spread = endSpreads[i];

                if (spread > 0f)
                {
                    end += particleSystemState.Random.NextInUnitBall(out _) * spread;
                }

                var delta = end - start;
                var length = delta.Length();

                var endDistance = endOffsets[i];
                if (endDistance != 0f)
                {
                    end += delta * (endDistance / (length + ParticleMath.FloatEpsilon));
                    delta = end - start;
                    length = delta.Length();
                }

                var startDistance = startOffsets[i];
                if (startDistance != 0f)
                {
                    start += delta * (startDistance / (length + ParticleMath.FloatEpsilon));
                    delta = end - start;
                    length = delta.Length();
                    positions[i] = start;
                }

                var speed = particleSystemState.Random.NextBetween(speedMins[i], speedMaxs[i]);
                var trailLength = trailBias ? trailLengths[i] : 0f;

                lifetimes[i] = (length / (speed + ParticleMath.FloatEpsilon)) + trailLength;
                previousPositions[i] = positions[i] - (delta * (speed / (length + ParticleMath.FloatEpsilon)) * particleSystemState.PreviousStep);
            }
        }
    }
}
