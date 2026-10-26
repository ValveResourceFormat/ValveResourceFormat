namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Pulls particles toward their spawn position held relative to a control point, so they follow
    /// the control point with the offset they spawned at. The offset is stored in a cache attribute
    /// the first time the operator sees the particle, in the control point's unscaled frame, and the
    /// scales apply on the way back to world space.
    /// </summary>
    /// <remarks>
    /// A cache attribute still equal to its initial value counts as unset, so the cache must be an
    /// attribute no initializer writes.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpToInitialPosition">C_OP_LerpToInitialPosition</seealso>
    class LerpToInitialPosition : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);
        private readonly ParticleField cacheField = ParticleField.HitboxOffsetPosition;
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly IVectorProvider vectorScale = new LiteralVectorProvider(Vector3.One);

        public LerpToInitialPosition(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            cacheField = parse.ParticleField("m_nCacheField", cacheField);
            scale = parse.CollectionNumberProvider("m_flScale", scale);
            vectorScale = parse.CollectionVectorProvider("m_vecScale", vectorScale);

            parse.AddInitialRead(cacheField);
            parse.AddInitialRead(ParticleField.Position);
        }

        /// <inheritdoc/>
        public override ulong StoredFields
            => cacheField.Mask() | ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var point = particleSystemState.GetControlPoint(controlPoint);
            var toWorld = point.GetTransform();

            if (!Matrix4x4.Invert(toWorld, out var toLocal))
            {
                toLocal = Matrix4x4.Identity;
            }

            var current = particles.Current;
            var interpolations = interpolation.Fill(current, particleSystemState);
            var uniformScale = scale.NextNumber(particleSystemState);
            var localScale = vectorScale.NextVector(particleSystemState) * uniformScale;

            if (!ParticleBuffer.IsVector(cacheField))
            {
                return;
            }

            var caches = current.Vectors(cacheField);
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var initialCaches = particles.Initial.ReadVectors(cacheField);
            var initialPositions = particles.Initial.ReadVectors(ParticleField.Position);

            for (var i = 0; i < positions.Length; i++)
            {
                var cache = caches[i];

                if (Vector3.Distance(cache, initialCaches[i]) == 0f)
                {
                    cache = Vector3.Transform(initialPositions[i], toLocal);
                    caches[i] = cache;
                }

                var target = Vector3.Transform(cache * localScale, toWorld);
                var amount = MathUtils.Saturate(strength * interpolations[i]);

                positions[i] = MathUtils.Lerp(positions[i], target, amount);
                previousPositions[i] = MathUtils.Lerp(previousPositions[i], target, amount);
            }
        }
    }
}
