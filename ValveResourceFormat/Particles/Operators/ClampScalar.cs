using System.Numerics.Tensors;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps a scalar particle field to a configurable minimum and maximum range each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampScalar">C_OP_ClampScalar</seealso>
    class ClampScalar : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField outputField = ParticleField.Radius;

        public ClampScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var mins = outputMin.Fill(current, particleSystemState);
            var maxs = outputMax.Fill(current, particleSystemState);

            var outputs = current.Floats(outputField);
            var clamped = strength >= 1f ? outputs : ParticleScratch.Floats(outputs.Length);

            // An inverted range is not corrected: the bounds apply in order, so the maximum wins outright
            TensorPrimitives.Max(outputs, mins, clamped);
            TensorPrimitives.Min(clamped, maxs, clamped);

            if (strength < 1f)
            {
                TensorPrimitives.Lerp(outputs, clamped, strength, outputs);
            }
        }
    }
}
