namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Sets a scalar particle attribute to a per-particle float value, with an optional
    /// interpolation factor that blends between the current value and the target each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFloat">C_OP_SetFloat</seealso>
    class SetFloat : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            value = parse.NumberProvider("m_InputValue", value);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            lerp = parse.NumberProvider("m_Lerp", lerp);

            // there's also a Lerp value that every frame sets the value to the lerp of the current one to the set one.
            // Thus it's basically like exponential decay, except it works with the
            // initial value, which works because they store the init value
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var value = this.value.NextNumber(ref particle, particleSystemState);
                var lerp = MathUtils.Saturate(this.lerp.NextNumber(ref particle, particleSystemState));

                var target = particle.ModifyScalarBySetMethod(particles, outputField, value, setMethod);
                var currentValue = particle.GetScalar(outputField);

                var blended = float.Lerp(currentValue, target, lerp);

                // Strength lerps from the spawn initial for the two initial-value set methods
                var strengthBase = setMethod is ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE
                    ? particle.GetInitialScalar(particles, outputField)
                    : currentValue;

                particle.SetScalar(outputField, float.Lerp(strengthBase, blended, strength));
            }
        }
    }
}
