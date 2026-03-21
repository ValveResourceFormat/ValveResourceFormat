namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Adds a per-component random offset to the particle position. When the proportional flag is
    /// set, the offset is scaled by the particle's radius.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionOffset">C_INIT_PositionOffset</seealso>
    class PositionOffset : ParticleFunctionInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        //private readonly int controlPoint; // unknown what this does

        private readonly bool proportional; // offset proportional to radius 0/1

        public PositionOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.VectorProvider("m_OffsetMin", offsetMin);
            offsetMax = parse.VectorProvider("m_OffsetMax", offsetMax);
            proportional = parse.Boolean("m_bProportional", proportional);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {

            var offset = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID,
                offsetMin.NextVector(ref particle, particleSystemState),
                offsetMax.NextVector(ref particle, particleSystemState));

            if (proportional)
            {
                offset *= particle.Radius;
            }

            particle.Position += offset;

            return particle;
        }
    }
}
