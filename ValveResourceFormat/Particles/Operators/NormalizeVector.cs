using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Normalizes a vector particle attribute to unit length, then multiplies the result by a
    /// scale factor.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_NormalizeVector">C_OP_NormalizeVector</seealso>
    class NormalizeVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly float scale = 1.0f;

        public NormalizeVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            scale = parse.Float("m_flScale", scale);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var vectors = particles.Current.Vectors(outputField);

            for (var i = 0; i < vectors.Length; i++)
            {
                var vector = vectors[i];

                // Z carries an epsilon before the length is taken, so a vector with no direction
                // comes out pointing along Z rather than staying at zero
                vector.Z += ParticleMath.FloatEpsilon;

                vectors[i] = MathUtils.SafeNormalize(vector) * scale;
            }
        }
    }
}
