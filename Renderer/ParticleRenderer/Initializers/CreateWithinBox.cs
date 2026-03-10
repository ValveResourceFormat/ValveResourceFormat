namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within an axis-aligned box defined by a minimum and
    /// maximum corner vector, offset by a control point. An optional scale control point can
    /// uniformly scale the box extents. Corresponds to <c>C_INIT_CreateWithinBox</c>.
    /// </summary>
    class CreateWithinBox : ParticleFunctionInitializer
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);

        private readonly int controlPointNumber;
        private readonly int scaleCP = -1;

        public CreateWithinBox(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.VectorProvider("m_vecMin", min);
            max = parse.VectorProvider("m_vecMax", max);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            scaleCP = parse.Int32("m_nScaleCP", scaleCP);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var posMin = min.NextVector(ref particle, particleSystemState);
            var posMax = max.NextVector(ref particle, particleSystemState);

            var position = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, posMin, posMax);

            var offset = particleSystemState.GetControlPoint(controlPointNumber).Position;

            if (scaleCP > -1)
            {
                // Scale CP uses X position as scale value. Not applied to the CP Offset
                position *= particleSystemState.GetControlPoint(scaleCP).Position.X;
            }

            particle.Position += position + offset;

            return particle;
        }
    }
}
