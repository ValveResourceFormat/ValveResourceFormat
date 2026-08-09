namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Rotates a vector particle attribute each frame around an axis chosen randomly per particle,
    /// at a rate chosen randomly per particle within configurable min/max ranges.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RotateVector">C_OP_RotateVector</seealso>
    class RotateVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly Vector3 rotAxisMin = new(0, 0, 1);
        private readonly Vector3 rotAxisMax = new(0, 0, 1);

        private readonly float rotRateMin = 180f;
        private readonly float rotRateMax = 180f;

        private readonly INumberProvider perParticleScale = new LiteralNumberProvider(1f);
        private readonly bool normalize = true;

        public RotateVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            rotAxisMin = parse.Vector3("m_vecRotAxisMin", rotAxisMin);
            rotAxisMax = parse.Vector3("m_vecRotAxisMax", rotAxisMax);
            rotRateMin = parse.Float("m_flRotRateMin", rotRateMin);


            rotRateMax = parse.Float("m_flRotRateMax", rotRateMax);
            perParticleScale = parse.NumberProvider("m_flScale", perParticleScale);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        private static Vector3 MatrixMul(Vector3 vector, Matrix4x4 rotatedMatrix)
        {
            return vector.X * new Vector3(rotatedMatrix.M11, rotatedMatrix.M12, rotatedMatrix.M13) +
                vector.Y * new Vector3(rotatedMatrix.M21, rotatedMatrix.M22, rotatedMatrix.M23) +
                vector.Z * new Vector3(rotatedMatrix.M31, rotatedMatrix.M32, rotatedMatrix.M33);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                // The rotation rate shares the axis draw rather than taking one of its own
                var random = particleSystemState.Random.ForParticle(particle.ParticleId);
                var axis = Vector3.Normalize(Vector3.Lerp(rotAxisMin, rotAxisMax, random));
                var rotationRate = float.DegreesToRadians(float.Lerp(rotRateMin, rotRateMax, random));

                var scale = perParticleScale.NextNumber(ref particle, particleSystemState);

                // probably slow but who knows???
                var currentVector = particle.GetVector(outputField);
                var rotatedVector = MatrixMul(currentVector, Matrix4x4.CreateFromAxisAngle(axis, rotationRate * scale * frameTime));

                rotatedVector = normalize
                    ? Vector3.Normalize(rotatedVector)
                    : rotatedVector;

                particle.SetVector(outputField, Vector3.Lerp(currentVector, rotatedVector, strength));
            }
        }
    }
}
