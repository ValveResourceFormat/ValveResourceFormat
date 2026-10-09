namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the visibility of a transform input's position into a vector output field: the
    /// visibility becomes a single 0-1 fraction that lerps componentwise between
    /// <c>m_vecOutputMin</c> and <c>m_vecOutputMax</c>, folded through the set method and lerped
    /// toward by the operator strength. The remapped value is computed once per frame and is the
    /// same for every particle.
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
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var fraction = MathUtils.RemapValClamped(RemapTransformVisibilityToScalar.FullyVisible, inputMin, inputMax, 0f, 1f);
            var value = Vector3.Lerp(outputMin, outputMax, fraction);

            foreach (ref var particle in particles.Current)
            {
                var current = particle.GetVector(outputField);
                var final = particle.ModifyVectorBySetMethod(particles, outputField, value, setMethod);
                particle.SetVector(outputField, current + ((final - current) * strength));
            }
        }
    }
}
