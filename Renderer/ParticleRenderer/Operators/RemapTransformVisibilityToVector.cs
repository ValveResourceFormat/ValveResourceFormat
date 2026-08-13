namespace ValveResourceFormat.Renderer.Particles.Operators
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
    /// even for color outputs. Visibility comes from the same always-fully-visible query, so the
    /// schema's <c>m_flRadius</c> never influences the result.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformVisibilityToVector">C_OP_RemapTransformVisibilityToVector</seealso>
    class RemapTransformVisibilityToVector : ParticleFunctionOperator
    {
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;

        public RemapTransformVisibilityToVector(ParticleDefinitionParser parse) : base(parse)
        {
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var visibility = RemapTransformVisibilityToScalar.QueryVisibility(transformInput.NextTransform(particleSystemState).Translation);

            var fraction = inputMin == inputMax
                ? (visibility - inputMax >= 0f ? 1f : 0f)
                : MathUtils.Saturate((visibility - inputMin) / (inputMax - inputMin));
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
