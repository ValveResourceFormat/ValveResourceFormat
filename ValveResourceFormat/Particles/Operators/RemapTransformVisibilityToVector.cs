namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps transform visibility into a vector output field as a 0-1 fraction lerping componentwise
    /// between <c>m_vecOutputMin</c> and <c>m_vecOutputMax</c>, folded through the set method and
    /// lerped by strength. Computed once per frame, shared by all particles.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="RemapTransformVisibilityToScalar"/> the output range is never clamped, not
    /// even for color outputs. Visibility always reads as full, so the transform input and
    /// <c>m_flRadius</c> have no effect.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformVisibilityToVector">C_OP_RemapTransformVisibilityToVector</seealso>
    class RemapTransformVisibilityToVector : ParticleFunctionOperator
    {
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;

        public RemapTransformVisibilityToVector(ParticleDefinitionParser parse) : base(parse)
        {
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var fraction = MathUtils.RemapValClamped(RemapTransformVisibilityToScalar.FullyVisible, inputMin, inputMax, 0f, 1f);
            var value = Vector3.Lerp(outputMin, outputMax, fraction);

            // All three components are written into scalar fields too, spilling into the same field of the particles 4 and 8 slots later
            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var output = particles.Current.Vectors(outputField);
            var initial = particles.Initial.ReadVectors(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                var current = output[i];
                var final = ParticleFieldExtensions.ApplySetMethod(value, initial[i], current, particleSystemState.CurrentStep, setMethod);
                output[i] = current + ((final - current) * strength);
            }
        }
    }
}
