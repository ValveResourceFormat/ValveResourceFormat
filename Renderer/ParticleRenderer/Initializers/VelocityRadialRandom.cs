namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particle velocity to a random speed directed radially outward from a control point.
    /// The speed is scaled per-axis by a local coordinate system scale vector.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityRadialRandom">C_INIT_VelocityRadialRandom</seealso>
    class VelocityRadialRandom : ParticleFunctionInitializer
    {
        // unsure if this is actually a vector provider
        private readonly IVectorProvider vectorScale = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0.1f);

        private readonly int controlPoint;
        private readonly bool ignoreDelta;

        public VelocityRadialRandom(ParticleDefinitionParser parse) : base(parse)
        {
            vectorScale = parse.VectorProvider("m_vecLocalCoordinateSystemSpeedScale", vectorScale);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            ignoreDelta = parse.Boolean("m_bIgnoreDelta", ignoreDelta);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var speedmin = speedMin.NextNumber(ref particle, particleSystemState);
            var speedmax = speedMax.NextNumber(ref particle, particleSystemState);

            var speed = Math.Max(1.0f, ParticleCollection.RandomBetween(particle.ParticleID, speedmin, speedmax));

            // With the flag set the authored value is a raw per-step displacement, not units/second.
            if (ignoreDelta)
            {
                var frameTime = particleSystemState.Data?.CurrentFrameTime ?? 0f;
                if (frameTime > 0f)
                {
                    speed /= frameTime;
                }
            }

            var scale = vectorScale.NextVector(ref particle, particleSystemState);

            var direction = Vector3.Normalize(particle.Position - particleSystemState.GetControlPoint(controlPoint).Position);

            particle.Velocity += direction * speed * scale;

            return particle;
        }
    }
}
