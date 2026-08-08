namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Writes each particle's velocity, taken from the change in position over the step, into a vector
    /// field. By default the velocity is expressed in units per second and scaled by <c>m_flScale</c>;
    /// with <c>m_bNormalize</c> only the direction is kept, scaled by <c>m_flScale</c>, and particles
    /// that have not moved keep their existing value.
    ///
    /// <para><see cref="ParticleField.Position"/> and <see cref="ParticleField.PositionPrevious"/> are
    /// rejected as outputs, which turns the operator into a no-op.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVelocityToVector">C_OP_RemapVelocityToVector</seealso>
    class RemapVelocityToVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly float scale = 1f;
        private readonly bool normalize;

        public RemapVelocityToVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            scale = parse.Float("m_flScale", scale);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (outputField is ParticleField.Position or ParticleField.PositionPrevious)
            {
                return;
            }

            // The Verlet position pair encodes the previous step's displacement, so it is the
            // previous step's time that turns it back into units per second
            var perSecond = scale / MathF.Max(1e-20f, particles.PreviousFrameTime);

            foreach (ref var particle in particles.Current)
            {
                var velocity = particle.Position - particle.PositionPrevious;

                if (normalize)
                {
                    if (velocity != Vector3.Zero)
                    {
                        particle.SetVector(outputField, Vector3.Normalize(velocity) * scale);
                    }
                }
                else
                {
                    particle.SetVector(outputField, velocity * perSecond);
                }
            }
        }
    }
}
