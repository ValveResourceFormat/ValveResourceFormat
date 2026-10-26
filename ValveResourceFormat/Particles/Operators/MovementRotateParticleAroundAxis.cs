namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Rotates particles around an axis anchored at a transform input, moving both ends of the
    /// Verlet position pair so the orbital motion does not read back as velocity. The axis is in
    /// world space unless marked local, in which case it follows the transform's orientation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementRotateParticleAroundAxis">C_OP_MovementRotateParticleAroundAxis</seealso>
    class MovementRotateParticleAroundAxis : ParticleFunctionOperator
    {
        private readonly IVectorProvider rotationAxis = new LiteralVectorProvider(Vector3.UnitZ);
        private readonly INumberProvider rotationRate = new LiteralNumberProvider(180f);
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly bool localSpace;

        public MovementRotateParticleAroundAxis(ParticleDefinitionParser parse) : base(parse)
        {
            rotationAxis = parse.VectorProvider("m_vecRotAxis", rotationAxis);
            rotationRate = parse.NumberProvider("m_flRotRate", rotationRate);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var angle = float.DegreesToRadians(rotationRate.NextNumber(particleSystemState)) * frameTime;
            var axis = MathUtils.SafeNormalize(rotationAxis.NextVector(particleSystemState), Vector3.UnitZ);

            var transform = transformInput.NextTransform(particleSystemState);

            if (localSpace)
            {
                axis = Vector3.TransformNormal(axis, transform);
            }

            var center = transform.Translation;
            var rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < positions.Length; i++)
            {
                var rotated = center + Vector3.Transform(positions[i] - center, rotation);
                var rotatedPrevious = center + Vector3.Transform(previousPositions[i] - center, rotation);

                positions[i] = Vector3.Lerp(positions[i], rotated, strength);
                previousPositions[i] = Vector3.Lerp(previousPositions[i], rotatedPrevious, strength);
            }
        }
    }
}
