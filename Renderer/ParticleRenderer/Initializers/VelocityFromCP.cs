namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particle velocity from a vector input, optionally normalizing direction and applying a transform.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityFromCP">C_INIT_VelocityFromCP</seealso>
    class VelocityFromCP : ParticleFunctionInitializer
    {
        private readonly IVectorProvider velocityInput = new LiteralVectorProvider(Vector3.Zero);
        private readonly ITransformProvider transformInput = new IdentityTransformProvider();
        private readonly float velocityScale = 1f;
        private readonly bool directionOnly;

        public VelocityFromCP(ParticleDefinitionParser parse) : base(parse)
        {
            velocityInput = parse.VectorProvider("m_velocityInput", velocityInput);
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
            directionOnly = parse.Boolean("m_bDirectionOnly", directionOnly);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemRenderState)
        {
            var velocity = velocityInput.NextVector(ref particle, particleSystemRenderState);

            if (directionOnly && velocity != Vector3.Zero)
            {
                velocity = Vector3.Normalize(velocity);
            }

            velocity *= velocityScale;
            velocity = Vector3.TransformNormal(velocity, transformInput.NextTransform(ref particle, particleSystemRenderState));

            particle.Velocity = velocity;
            return particle;
        }
    }
}
