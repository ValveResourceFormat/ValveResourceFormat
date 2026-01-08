namespace ValveResourceFormat.Renderer.Particles.Operators
{
    class RotateVector : ParticleFunctionOperator
    {
        private readonly ParticleField OutputField = ParticleField.Normal;
        private readonly Vector3 RotAxisMin = new(0, 0, 1);
        private readonly Vector3 RotAxisMax = new(0, 0, 1);

        private readonly float RotRateMin = 180f;
        private readonly float rotRateMax = 180f;

        private readonly INumberProvider perParticleScale = new LiteralNumberProvider(1f);
        private readonly bool normalize;

        public RotateVector(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nFieldOutput", OutputField);
            RotAxisMin = parse.Vector3("m_vecRotAxisMin", RotAxisMin);
            RotAxisMax = parse.Vector3("m_vecRotAxisMax", RotAxisMax);
            RotRateMin = parse.Float("m_flRotRateMin", RotRateMin);


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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var axis = Vector3.Normalize(ParticleCollection.RandomBetween(particle.ParticleID, RotAxisMin, RotAxisMax));
                var rotationRate = MathUtils.ToRadians(ParticleCollection.RandomBetween(particle.ParticleID, RotRateMin, rotRateMax));

                var scale = perParticleScale.NextNumber(ref particle, particleSystemState);

                // probably slow but who knows???
                var rotatedVector = MatrixMul(particle.GetVector(OutputField), Matrix4x4.CreateFromAxisAngle(axis, rotationRate * scale * frameTime));

                rotatedVector = normalize
                    ? Vector3.Normalize(rotatedVector)
                    : rotatedVector;

                particle.SetVector(OutputField, rotatedVector);
            }
        }
    }
}
