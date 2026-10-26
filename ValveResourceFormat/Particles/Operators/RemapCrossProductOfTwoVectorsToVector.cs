namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Computes the cross product of two input vectors and writes the result into a vector
    /// particle attribute, with an option to normalize the output.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapCrossProductOfTwoVectorsToVector">C_OP_RemapCrossProductOfTwoVectorsToVector</seealso>
    class RemapCrossProductOfTwoVectorsToVector : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly IVectorProvider inputVec1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider inputVec2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly bool normalize;

        public RemapCrossProductOfTwoVectorsToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputVec1 = parse.VectorProvider("m_InputVec1", inputVec1);
            inputVec2 = parse.VectorProvider("m_InputVec2", inputVec2);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var output = current.Vectors(fieldOutput);
            current.FillVectorInputs(inputVec1, inputVec2, particleSystemState, out var vectors1, out var vectors2);

            for (var i = 0; i < output.Length; i++)
            {
                var cross = Vector3.Cross(vectors1[i], vectors2[i]);

                if (normalize)
                {
                    cross = MathUtils.SafeNormalize(cross);
                }

                output[i] = cross;
            }
        }
    }
}
