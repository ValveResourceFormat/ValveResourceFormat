namespace GUI.Types.ParticleRenderer.Initializers
{
    class PositionOffset : ParticleFunctionInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        //private readonly int controlPoint; // unknown what this does

        private readonly bool proportional;

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
