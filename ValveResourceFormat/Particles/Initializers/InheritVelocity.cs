namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Inherits velocity from a control point, optionally scaling the inherited value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InheritVelocity">C_INIT_InheritVelocity</seealso>
    class InheritVelocity : ParticleFunctionInitializer
    {
        private readonly int controlPointNumber;
        private readonly float velocityScale = 1f;

        public InheritVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var frameTime = particles.CurrentFrameTime;

            if (frameTime <= 0f)
            {
                return particle;
            }

            // A rescaled move of 100 units or more is treated as a teleport
            var step = particleSystemState.GetControlPoint(controlPointNumber).StepDelta * particles.PreviousFrameTime / frameTime;

            if (step.Length() >= 100f)
            {
                return particle;
            }

            particle.PositionPrevious -= step * velocityScale;
            return particle;
        }
    }
}
