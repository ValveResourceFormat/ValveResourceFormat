using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles on an epitrochoid around a transform, in the plane of two chosen axes. The curve
    /// parameter is the particle's creation time, or a running count of the particles placed, times the
    /// density, and the position can instead offset the existing one.
    /// </summary>
    /// <remarks>
    /// Both terms of the curve are added, where the classical epitrochoid subtracts the second, and an
    /// axis of -1 leaves that term out. Placed by creation time, particles created at the same moment
    /// share a point. The transform is read once per emission, and the count keeps running across
    /// restarts.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateInEpitrochoid">C_INIT_CreateInEpitrochoid</seealso>
    class CreateInEpitrochoid : ParticleFunctionInitializer
    {
        private readonly int component1;
        private readonly int component2 = 1;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly INumberProvider particleDensity = new LiteralNumberProvider(10f);
        private readonly INumberProvider offset = new LiteralNumberProvider(4f);
        private readonly INumberProvider radius1 = new LiteralNumberProvider(40f);
        private readonly INumberProvider radius2 = new LiteralNumberProvider(24f);
        private readonly bool useCount;
        private readonly bool useLocalCoords;
        private readonly bool offsetExistingPos;

        private int count;
        private Matrix4x4 transform;

        public CreateInEpitrochoid(ParticleDefinitionParser parse) : base(parse)
        {
            component1 = Math.Clamp(parse.Int32("m_nComponent1", component1), -1, 2);
            component2 = Math.Clamp(parse.Int32("m_nComponent2", component2), -1, 2);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            particleDensity = parse.NumberProvider("m_flParticleDensity", particleDensity);
            offset = parse.NumberProvider("m_flOffset", offset);
            radius1 = parse.NumberProvider("m_flRadius1", radius1);
            radius2 = parse.NumberProvider("m_flRadius2", radius2);
            useCount = parse.Boolean("m_bUseCount", useCount);
            useLocalCoords = parse.Boolean("m_bUseLocalCoords", useLocalCoords);
            offsetExistingPos = parse.Boolean("m_bOffsetExistingPos", offsetExistingPos);
        }

        public override void Reset()
        {
            count = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            transform = transformInput.NextTransform(particleSystemState);
            base.Initialize(batch, particles, particleSystemState);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var rollingRadius = radius2.NextNumber(ref particle, particleSystemState) + ParticleMath.FloatEpsilon;
            var outerRadius = radius1.NextNumber(ref particle, particleSystemState) + rollingRadius;
            var penOffset = offset.NextNumber(ref particle, particleSystemState);
            var density = particleDensity.NextNumber(ref particle, particleSystemState);

            var t = useCount ? count++ * density : density * particle.CreationTime;
            var rollAngle = t / rollingRadius * outerRadius;

            var curve = Vector3.Zero;

            if (component1 != -1)
            {
                curve[component1] = (MathF.Cos(t) * outerRadius) + (MathF.Cos(rollAngle) * penOffset);
            }

            if (component2 != -1)
            {
                curve[component2] = (MathF.Sin(t) * outerRadius) + (MathF.Sin(rollAngle) * penOffset);
            }

            if (useLocalCoords)
            {
                curve = Vector3.TransformNormal(curve, transform);
            }

            if (offsetExistingPos)
            {
                particle.Position += curve;
                particle.PositionPrevious += curve;
            }
            else
            {
                particle.Position = transform.Translation + curve;
                particle.PositionPrevious = transform.Translation + curve;
            }

            return particle;
        }
    }
}
