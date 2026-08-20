using ValveResourceFormat.Particles.Utils;

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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var authoredAxis = rotationAxis.NextVector(particleSystemState);

            // The whole axis is replaced when its x is zero, so a pure Y axis rotates about Z
            var axis = authoredAxis.X == 0f ? Vector3.UnitZ : ParticleMath.Normalize(authoredAxis);

            var transform = transformInput.NextTransform(particleSystemState);

            if (localSpace)
            {
                axis = Vector3.TransformNormal(axis, transform);
            }

            var center = transform.Translation;
            var angle = float.DegreesToRadians(rotationRate.NextNumber(particleSystemState)) * frameTime;
            var rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);

            foreach (ref var particle in particles.Current)
            {
                var rotated = center + Vector3.Transform(particle.Position - center, rotation);
                var rotatedPrevious = center + Vector3.Transform(particle.PositionPrevious - center, rotation);

                particle.Position = Vector3.Lerp(particle.Position, rotated, strength);
                particle.PositionPrevious = Vector3.Lerp(particle.PositionPrevious, rotatedPrevious, strength);
            }
        }
    }
}
