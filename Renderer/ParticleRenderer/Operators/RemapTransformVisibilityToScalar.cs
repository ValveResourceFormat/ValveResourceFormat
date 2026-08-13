namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps the visibility of a transform input's position into a scalar output field, folded
    /// through the set method and lerped toward by the operator strength. The remapped value is
    /// computed once per frame and is the same for every particle.
    /// </summary>
    /// <remarks>
    /// <c>m_flOutputMin</c>/<c>m_flOutputMax</c> are clamped into [0, 1] at load time when the
    /// output field is alpha or alternate alpha, matching the engine mutating its stored fields.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformVisibilityToScalar">C_OP_RemapTransformVisibilityToScalar</seealso>
    class RemapTransformVisibilityToScalar : ParticleFunctionOperator
    {
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapTransformVisibilityToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var visibility = QueryVisibility(transformInput.NextTransform(particleSystemState).Translation);

            var value = inputMin == inputMax
                ? (visibility - inputMax >= 0f ? outputMax : outputMin)
                : (MathUtils.Saturate((visibility - inputMin) / (inputMax - inputMin)) * (outputMax - outputMin)) + outputMin;

            foreach (ref var particle in particles.Current)
            {
                var current = particle.GetScalar(outputField);
                var final = particle.ModifyScalarBySetMethod(particles, outputField, value, setMethod);
                particle.SetScalar(outputField, current + ((final - current) * strength));
            }
        }

        /// <summary>
        /// Visibility of the transform's position. Valve's tools-side scene query is a stub that
        /// always reports fully visible, and VRF matches it, so the queried position and the
        /// schema's <c>m_flRadius</c> never influence the result.
        /// </summary>
        internal static float QueryVisibility(Vector3 transformPosition) => 1f;
    }
}
