namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes a control point's velocity over the previous step, scaled, into a vector attribute of
    /// every particle. With <c>m_bNormalize</c> only its direction is kept.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapCPVelocityToVector">C_OP_RemapCPVelocityToVector</seealso>
    class RemapCPVelocityToVector : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float scale = 1f;
        private readonly bool normalize;

        public RemapCPVelocityToVector(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            scale = parse.Float("m_flScale", scale);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var delta = particleSystemState.GetControlPoint(controlPoint).StepDelta;
            var value = normalize
                ? MathUtils.SafeNormalize(delta) * scale
                : delta * (scale / MathF.Max(1e-20f, particles.PreviousFrameTime));

            foreach (var particle in particles.Current)
            {
                particle.SetVector(fieldOutput, value);
            }
        }
    }
}
