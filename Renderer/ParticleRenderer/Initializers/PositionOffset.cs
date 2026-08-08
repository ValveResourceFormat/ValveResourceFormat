namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Adds a per-component random offset to the particle position. With local coordinates the
    /// offset is authored in a transform input's frame and rotates with it; when the proportional
    /// flag is set, the offset is scaled by the particle's radius.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionOffset">C_INIT_PositionOffset</seealso>
    class PositionOffset : ParticleFunctionInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly bool localCoords; // offset in local space 0/1
        private readonly bool proportional; // offset proportional to radius 0/1

        public PositionOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.VectorProvider("m_OffsetMin", offsetMin);
            offsetMax = parse.VectorProvider("m_OffsetMax", offsetMax);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            proportional = parse.Boolean("m_bProportional", proportional);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {

            var offset = particleSystemState.NextRandomBetweenPerComponent(
                offsetMin.NextVector(ref particle, particleSystemState),
                offsetMax.NextVector(ref particle, particleSystemState));

            if (proportional)
            {
                offset *= particle.Radius;
            }

            if (localCoords)
            {
                offset = Vector3.TransformNormal(offset, transformInput.NextTransform(ref particle, particleSystemState));
            }

            particle.Position += offset;

            return particle;
        }
    }
}
