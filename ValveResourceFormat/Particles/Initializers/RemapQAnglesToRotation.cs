namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the pitch, yaw and roll of every new particle from the position of a transform input read
    /// as (pitch, yaw, roll) angles in degrees, such as a control point whose position the game sets to
    /// an angle triple. The transform's orientation is ignored.
    /// </summary>
    /// <remarks>
    /// The transform is read once per function call, so all particles of a call get the same angles.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapQAnglesToRotation">C_INIT_RemapQAnglesToRotation</seealso>
    class RemapQAnglesToRotation : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        public RemapQAnglesToRotation(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
        }

        public override ulong WrittenFields => ParticleField.Roll.Mask() | ParticleField.Yaw.Mask() | ParticleField.Pitch.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var degrees = transformInput.NextTransform(particleSystemState).Translation;

            run.Floats(ParticleField.Pitch).Fill(float.DegreesToRadians(degrees.X));
            run.Floats(ParticleField.Yaw).Fill(float.DegreesToRadians(degrees.Y));
            run.Floats(ParticleField.Roll).Fill(float.DegreesToRadians(degrees.Z));
        }
    }
}
