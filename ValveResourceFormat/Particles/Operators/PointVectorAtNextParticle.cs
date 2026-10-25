namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lerps a vector particle attribute toward the unit direction from each particle to the next
    /// (or, with m_bPrevious, the previous) particle in the collection. The edge particle that has
    /// no neighbor in that direction copies its neighbor's resulting value instead.
    /// </summary>
    /// <remarks>
    /// The interpolation is evaluated in particle order but handed out only to the particles that
    /// have a neighbor, so with <c>m_bPrevious</c> particle <c>i</c> lerps by the value of particle <c>i - 1</c>.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PointVectorAtNextParticle">C_OP_PointVectorAtNextParticle</seealso>
    class PointVectorAtNextParticle : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);
        private readonly bool previous;

        public PointVectorAtNextParticle(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            previous = parse.Boolean("m_bPrevious", previous);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particles.Count < 2)
            {
                return;
            }

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var output = current.Vectors(outputField);
            var step = previous ? -1 : 1;
            var edge = previous ? 0 : particles.Count - 1;
            var processed = 0;

            var interpolations = interpolation.Fill(current, particleSystemState);

            for (var i = 0; i < particles.Count; i++)
            {
                if (i == edge)
                {
                    continue;
                }

                var direction = positions[i + step] - positions[i];
                var interp = interpolations[processed++];
                output[i] = Vector3.Lerp(output[i], MathUtils.SafeNormalize(direction), interp);
            }

            output[edge] = output[edge - step];
        }
    }
}
