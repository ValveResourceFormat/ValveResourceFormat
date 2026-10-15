namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles at positions copied from a sequence of control points. A cursor steps through
    /// the range by the increment and carries over between emissions, going back to the first control
    /// point once it passes the last.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromCPs">C_INIT_CreateFromCPs</seealso>
    class CreateFromCPs : ParticleFunctionInitializer
    {
        private readonly int increment = 1;
        private readonly int minControlPoint;
        private readonly int maxControlPoint;
        private readonly bool useHighestControlPoint;
        private readonly INumberProvider dynamicControlPointCount = new LiteralNumberProvider(-1);

        private int currentControlPoint;

        public CreateFromCPs(ParticleDefinitionParser parse) : base(parse)
        {
            increment = parse.Int32("m_nIncrement", increment);
            minControlPoint = parse.Int32("m_nMinCP", minControlPoint);
            maxControlPoint = parse.BehaviorVersion < 2 ? 63 : parse.Int32("m_nMaxCP", maxControlPoint);
            useHighestControlPoint = parse.BehaviorVersion < 2;
            dynamicControlPointCount = parse.CollectionNumberProvider("m_nDynamicCPCount", dynamicControlPointCount);

            if (maxControlPoint < minControlPoint)
            {
                (minControlPoint, maxControlPoint) = (maxControlPoint, minControlPoint);
            }

            Reset();
        }

        public override void Reset()
        {
            currentControlPoint = minControlPoint;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.LifeDuration) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var max = useHighestControlPoint ? particleSystemState.HighestControlPoint : maxControlPoint;
            var count = max - minControlPoint + 1;
            var dynamicCount = (int)dynamicControlPointCount.NextNumber(particleSystemState);

            if (dynamicCount >= 0)
            {
                count = Math.Min(dynamicCount, count);
                max = count + minControlPoint - 1;
            }

            if (count == 0)
            {
                particle.Lifetime = 0f;
                return particle;
            }

            if (currentControlPoint > max)
            {
                currentControlPoint = minControlPoint;
            }

            var position = particleSystemState.GetControlPoint(currentControlPoint).GetPositionAtTime(particle.CreationTime);
            particle.Position = position;
            particle.PositionPrevious = position;

            currentControlPoint += increment;

            return particle;
        }
    }
}
