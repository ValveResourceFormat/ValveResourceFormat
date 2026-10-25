using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps each component of a vector attribute between two per-particle vector inputs.
    /// </summary>
    /// <remarks>
    /// An inverted range is not corrected: the minimum is applied first, so the maximum wins.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampVector">C_OP_ClampVector</seealso>
    class ClampVector : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);

        public ClampVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
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
            current.FillVectorInputs(outputMin, outputMax, particleSystemState, out var mins, out var maxs);

            var outputs = MemoryMarshal.Cast<Vector3, float>(current.Vectors(fieldOutput));
            var clamped = strength >= 1f ? outputs : ParticleScratch.Floats(outputs.Length);

            TensorPrimitives.Max(outputs, MemoryMarshal.Cast<Vector3, float>(mins), clamped);
            TensorPrimitives.Min(clamped, MemoryMarshal.Cast<Vector3, float>(maxs), clamped);

            if (strength < 1f)
            {
                TensorPrimitives.Lerp(outputs, clamped, strength, outputs);
            }
        }
    }
}
