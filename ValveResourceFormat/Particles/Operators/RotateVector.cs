using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
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
            rotAxisMin = MathUtils.SafeNormalize(parse.Vector3("m_vecRotAxisMin", rotAxisMin), Vector3.UnitZ);
            rotAxisMax = MathUtils.SafeNormalize(parse.Vector3("m_vecRotAxisMax", rotAxisMax), Vector3.UnitZ);
            rotRateMin = parse.Float("m_flRotRateMin", rotRateMin);

            rotRateMax = parse.Float("m_flRotRateMax", rotRateMax);
            perParticleScale = parse.NumberProvider("m_flScale", perParticleScale);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var scales = perParticleScale.Fill(current, particleSystemState);

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var output = current.Vectors(outputField);
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);

            for (var i = 0; i < output.Length; i++)
            {
                // The rotation rate shares the axis draw rather than taking one of its own
                var random = ParticleRandom.ForSample(particleSystemState.Random.Seed + particleIds[i]);
                var axis = MathUtils.SafeNormalize(MathUtils.Lerp(rotAxisMin, rotAxisMax, random));
                var rotationRate = float.DegreesToRadians(MathUtils.Lerp(rotRateMin, rotRateMax, random));

                var currentVector = output[i];
                var rotatedVector = Vector3.TransformNormal(currentVector, Matrix4x4.CreateFromAxisAngle(axis, rotationRate * scales[i] * frameTime));

                if (normalize)
                {
                    rotatedVector = MathUtils.SafeNormalize(rotatedVector);
                }

                output[i] = Vector3.Lerp(currentVector, rotatedVector, strength);
            }
        }
    }
}
