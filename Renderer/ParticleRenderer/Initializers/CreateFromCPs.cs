using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at positions copied from a sequence of control points.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromCPs">C_INIT_CreateFromCPs</seealso>
    class CreateFromCPs : ParticleFunctionInitializer
    {
        private readonly int Increment = 1;
        private readonly int MinControlPoint;
        private readonly int MaxControlPoint;
        private readonly INumberProvider DynamicControlPointCount = new LiteralNumberProvider(-1);

        public CreateFromCPs(ParticleDefinitionParser parse) : base(parse)
        {
            Increment = parse.Int32("m_nIncrement", Increment);
            MinControlPoint = parse.Int32("m_nMinCP", MinControlPoint);
            MaxControlPoint = parse.Int32("m_nMaxCP", MaxControlPoint);
            DynamicControlPointCount = parse.NumberProvider("m_nDynamicCPCount", DynamicControlPointCount);
        }

        private int GetControlPointCount(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var dynamicCount = (int)DynamicControlPointCount.NextNumber(ref particle, particleSystemState);
            if (dynamicCount > 0)
            {
                return dynamicCount;
            }

            if (Increment <= 0 || MaxControlPoint < MinControlPoint)
            {
                return 1;
            }

            return ((MaxControlPoint - MinControlPoint) / Increment) + 1;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var count = GetControlPointCount(ref particle, particleSystemState);
            if (count <= 0)
            {
                count = 1;
            }

            var controlPointIndex = particle.ParticleID % count;
            var controlPoint = MinControlPoint + controlPointIndex * Math.Max(1, Increment);

            if (controlPoint > MaxControlPoint)
            {
                if (Increment > 0 && MaxControlPoint >= MinControlPoint)
                {
                    var rangeCount = ((MaxControlPoint - MinControlPoint) / Increment) + 1;
                    controlPoint = MinControlPoint + (controlPointIndex % rangeCount) * Increment;
                }
                else
                {
                    controlPoint = MinControlPoint;
                }
            }

            var position = particleSystemState.GetControlPoint(controlPoint).Position;
            particle.Position = position;
            particle.PositionPrevious = position;
            particle.Velocity = Vector3.Zero;

            return particle;
        }
    }
}
