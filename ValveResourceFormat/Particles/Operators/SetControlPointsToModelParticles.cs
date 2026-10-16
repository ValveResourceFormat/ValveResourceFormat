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
                Math.Clamp(parse.Int32("m_nFirstControlPoint"), 0, ParticleSystemState.MaxControlPoint),
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

            for (var k = 0; k < count; k++)
            {
                particleSystemState.SetControlPointValue(binding.FirstControlPoint + k, particles.Current[binding.FirstSourcePoint + k].Position);
            }
        }
    }

    /// <summary>A run of control points bound to the models of a run of particles.</summary>
    /// <param name="firstControlPoint">The first control point of the run.</param>
    /// <param name="numControlPoints">How many control points the run holds at most.</param>
    /// <param name="firstSourcePoint">The index of the particle the first control point follows.</param>
    /// <param name="attachment">The model attachment the control points sit on, or null to sit on the particles.</param>
    internal sealed class ModelParticleControlPoints(int firstControlPoint, int numControlPoints, int firstSourcePoint, string? attachment)
    {
        /// <summary>The first control point of the run.</summary>
        public int FirstControlPoint => firstControlPoint;

        /// <summary>The index of the particle the first control point follows.</summary>
        public int FirstSourcePoint => firstSourcePoint;

        /// <summary>The model attachment the control points sit on, or null to sit on the particles.</summary>
        public string? Attachment => attachment;

        /// <summary>How many control points are bound while the system holds <paramref name="particleCount"/> particles.</summary>
        public int ActiveCount(int particleCount)
            => firstSourcePoint < 0 ? 0 : Math.Max(0, Math.Min(Math.Min(numControlPoints, particleCount - firstSourcePoint), ParticleSystemState.MaxControlPoint + 1 - firstControlPoint));
    }
}
