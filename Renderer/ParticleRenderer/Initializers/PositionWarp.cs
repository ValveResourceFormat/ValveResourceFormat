namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Scales each particle's spawn position relative to a control point by a random per-particle
    /// warp vector between a minimum and maximum.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionWarp">C_INIT_PositionWarp</seealso>
    class PositionWarp : ParticleFunctionInitializer
    {
        private readonly IVectorProvider warpMin = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider warpMax = new LiteralVectorProvider(Vector3.One);
        private readonly int controlPointNumber;

        public PositionWarp(ParticleDefinitionParser parse) : base(parse)
        {
            warpMin = parse.VectorProvider("m_vecWarpMin", warpMin);
            warpMax = parse.VectorProvider("m_vecWarpMax", warpMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var min = warpMin.NextVector(ref particle, particleSystemState);
            var max = warpMax.NextVector(ref particle, particleSystemState);
            var warp = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, min, max);

            var origin = particleSystemState.GetControlPoint(controlPointNumber).Position;
            particle.Position = origin + ((particle.Position - origin) * warp);

            return particle;
        }
    }
}
