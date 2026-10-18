using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes particle velocity from a vector input, optionally normalizing direction and applying a transform.
    /// Every particle of one call gets the same velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityFromCP">C_INIT_VelocityFromCP</seealso>
    class VelocityFromCP : ParticleFunctionInitializer
    {
        private readonly IVectorProvider velocityInput = new CPValueVectorProvider(0);
        private readonly ITransformProvider transformInput = new IdentityTransformProvider();
        private readonly float velocityScale = 1f;
        private readonly bool directionOnly;

        private Vector3 displacement;

        public VelocityFromCP(ParticleDefinitionParser parse) : base(parse)
        {
            velocityInput = parse.CollectionVectorProvider("m_velocityInput", velocityInput);
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
            directionOnly = parse.Boolean("m_bDirectionOnly", directionOnly);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        protected override void BeginCall(ref Particle first, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var velocity = velocityInput.NextVector(particleSystemState);
            velocity = Vector3.TransformNormal(velocity, transformInput.NextTransform(particleSystemState));

            if (directionOnly)
            {
                velocity = MathUtils.SafeNormalize(velocity);
            }

            displacement = velocity * particles.PreviousFrameTime * velocityScale;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.PositionPrevious -= displacement;
            return particle;
        }
    }
}
