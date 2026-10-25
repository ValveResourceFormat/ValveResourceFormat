using System.Numerics.Tensors;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps transform visibility into a scalar output field, folded through the set method and
    /// lerped toward by the operator strength. Computed once per frame and shared by all particles.
    /// </summary>
    /// <remarks>
    /// <c>m_flOutputMin</c>/<c>m_flOutputMax</c> are clamped into [0, 1] at load time for alpha and
    /// alternate alpha output fields. Visibility always reads as full, so the transform input and
    /// <c>m_flRadius</c> have no effect.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformVisibilityToScalar">C_OP_RemapTransformVisibilityToScalar</seealso>
    class RemapTransformVisibilityToScalar : ParticleFunctionOperator
    {
        internal const float FullyVisible = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapTransformVisibilityToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (outputField.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var value = MathUtils.RemapValClamped(FullyVisible, inputMin, inputMax, outputMin, outputMax);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var output = particles.Current.Floats(outputField);
            var initial = particles.Initial.ReadFloats(outputField);

            // The value by the set method, then the change toward it at the run strength
            var changes = ParticleScratch.Floats(output.Length);
            changes.Fill(value);
            ParticleFieldExtensions.ApplySetMethod(changes, initial, output, particleSystemState.CurrentStep, setMethod);
            TensorPrimitives.Subtract(changes, output, changes);
            TensorPrimitives.MultiplyAdd(changes, strength, output, output);
        }
    }
}
