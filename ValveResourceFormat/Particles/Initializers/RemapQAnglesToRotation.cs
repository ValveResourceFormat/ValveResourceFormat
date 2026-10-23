namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the pitch, yaw and roll of every new particle from the position of a transform input read
    /// as (pitch, yaw, roll) angles in degrees, such as a control point whose position the game sets to
    /// an angle triple. The transform's orientation is ignored.
    /// </summary>
    /// <remarks>
    /// The transform is read once per emission, so all particles of an emission get the same angles.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapQAnglesToRotation">C_INIT_RemapQAnglesToRotation</seealso>
    class RemapQAnglesToRotation : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        private Vector3 angles;

        public RemapQAnglesToRotation(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Roll) | FieldMask(ParticleField.Yaw) | FieldMask(ParticleField.Pitch);

        public override void Initialize(ParticleSpan batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var degrees = transformInput.NextTransform(particleSystemState).Translation;
            angles = new Vector3(float.DegreesToRadians(degrees.X), float.DegreesToRadians(degrees.Y), float.DegreesToRadians(degrees.Z));

            base.Initialize(batch, particles, particleSystemState);
        }

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.SetScalar(ParticleField.Pitch, angles.X);
            particle.SetScalar(ParticleField.Yaw, angles.Y);
            particle.SetScalar(ParticleField.Roll, angles.Z);

            return particle;
        }
    }
}
