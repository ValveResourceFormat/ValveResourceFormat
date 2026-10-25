namespace ValveResourceFormat.Particles.Operators
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

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (outputField is ParticleField.Position or ParticleField.PositionPrevious || !ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            // Verlet positions hold the previous step's displacement, so divide by that step's time
            var perSecond = particleSystemState.PerPreviousSecond(scale);

            var current = particles.Current;
            var output = current.Vectors(outputField);
            var positions = current.ReadVectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);

            for (var i = 0; i < output.Length; i++)
            {
                var velocity = positions[i] - previousPositions[i];

                if (normalize)
                {
                    if (velocity != Vector3.Zero)
                    {
                        output[i] = MathUtils.SafeNormalize(velocity) * scale;
                    }
                }
                else
                {
                    output[i] = velocity * perSecond;
                }
            }
        }
    }
}
