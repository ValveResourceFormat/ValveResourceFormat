namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Binds a run of control points to a run of particles every step: each control point takes its
    /// particle's position, and the model the particle is drawn as becomes the control point's
    /// model, so child effects can attach to particle models. With <c>m_bAttachment</c> the
    /// position comes from a named attachment of that model instead, which whatever draws the
    /// models places.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointsToModelParticles">C_OP_SetControlPointsToModelParticles</seealso>
    class SetControlPointsToModelParticles : ParticleFunctionOperator
    {
        private readonly ModelParticleControlPoints binding;

        public SetControlPointsToModelParticles(ParticleDefinitionParser parse) : base(parse)
        {
            var attachment = parse.Boolean("m_bAttachment") ? parse.String("m_AttachmentName") : string.Empty;

            binding = new ModelParticleControlPoints(
                parse.ControlPoint("m_nFirstControlPoint"),
                parse.Int32("m_nNumControlPoints", 1),
                parse.Int32("m_nFirstSourcePoint"),
                attachment.Length > 0 ? attachment : null);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            if (!particleSystemState.ModelParticleControlPoints.Contains(binding))
            {
                particleSystemState.ModelParticleControlPoints.Add(binding);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var count = binding.ActiveCount(particles.Count);

            if (binding.Attachment != null)
            {
                return;
            }

            var current = particles.Current;

            for (var k = 0; k < count; k++)
            {
                particleSystemState.SetControlPointValue(binding.FirstControlPoint + k, current.ReadVector(ParticleField.Position, binding.FirstSourcePoint + k));
            }
        }
    }
}
