using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at positions copied from a sequence of control points.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromCPs">C_INIT_CreateFromCPs</seealso>
    class CreateFromCPs : ParticleFunctionInitializer
    {
        private readonly int increment = 1;
        private readonly int minControlPoint;
        private readonly int maxControlPoint;
        private readonly INumberProvider dynamicControlPointCount = new LiteralNumberProvider(-1);

        public CreateFromCPs(ParticleDefinitionParser parse) : base(parse)
        {
            increment = parse.Int32("m_nIncrement", increment);
            minControlPoint = parse.Int32("m_nMinCP", minControlPoint);
            maxControlPoint = parse.BehaviorVersion < 2 ? 63 : parse.Int32("m_nMaxCP", maxControlPoint);
            dynamicControlPointCount = parse.NumberProvider("m_nDynamicCPCount", dynamicControlPointCount);
        }

        private int GetControlPointCount(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var dynamicCount = (int)dynamicControlPointCount.NextNumber(ref particle, particleSystemState);
            if (dynamicCount > 0)
            {
                return dynamicCount;
            }

            if (increment <= 0 || maxControlPoint < minControlPoint)
            {
                return 1;
            }

            return ((maxControlPoint - minControlPoint) / increment) + 1;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var count = GetControlPointCount(ref particle, particleSystemState);
            if (count <= 0)
            {
                count = 1;
            }

            var controlPointIndex = particle.UniqueParticleId % count;
            var controlPoint = minControlPoint + controlPointIndex * Math.Max(1, increment);

            if (controlPoint > maxControlPoint)
            {
                if (increment > 0 && maxControlPoint >= minControlPoint)
                {
                    var rangeCount = ((maxControlPoint - minControlPoint) / increment) + 1;
                    controlPoint = minControlPoint + (controlPointIndex % rangeCount) * increment;
                }
                else
                {
                    controlPoint = minControlPoint;
                }
            }

            var position = particleSystemState.GetControlPoint(controlPoint).Position;
            particle.Position = position;
            particle.Velocity = Vector3.Zero;

            return particle;
        }
    }
}
