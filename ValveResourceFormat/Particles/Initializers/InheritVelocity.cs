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

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var frameTime = particleSystemState.CurrentStep;

            if (frameTime <= 0f)
            {
                return;
            }

            // A rescaled move of 100 units or more is treated as a teleport
            var step = particleSystemState.GetControlPoint(controlPointNumber).StepDelta * particleSystemState.PreviousStep / frameTime;

            if (step.Length() >= 100f)
            {
                return;
            }

            var previous = run.Vectors(ParticleField.PositionPrevious);
            var scaledStep = step * velocityScale;

            for (var i = 0; i < previous.Length; i++)
            {
                previous[i] -= scaledStep;
            }
        }
    }
}
