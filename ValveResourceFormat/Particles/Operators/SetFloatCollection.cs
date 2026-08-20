namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Sets a scalar particle attribute from a value read once for the whole collection, blended
    /// toward by an interpolation factor that is also read once, so the whole collection moves
    /// toward the same target each frame.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="SetFloat"/>, and not simply that operator with
    /// a wider input. The run strength multiplies both the value and the interpolation factor before
    /// either is used, rather than blending the finished result, so strength acts on the outcome
    /// twice over. An angle output is stored as authored rather than converted from degrees.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFloatCollection">C_OP_SetFloatCollection</seealso>
    class SetFloatCollection : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetFloatCollection(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            value = parse.NumberProvider("m_InputValue", value);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            lerp = parse.NumberProvider("m_Lerp", lerp);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particles.Count == 0)
            {
                return;
            }

            var input = value.NextNumber(particleSystemState) * strength;
            var blend = MathUtils.Saturate(lerp.NextNumber(particleSystemState) * strength);
            var (min, max) = outputField.SetFloatRange();

            // Replacing resolves to one number for the whole collection, taken from the first
            // particle's value alone, which every other particle then receives regardless of its own
            if (setMethod == ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE)
            {
                var first = particles.Current[0].GetScalar(outputField);
                var replacement = Math.Clamp(float.Lerp(first, input, blend), min, max);

                foreach (ref var particle in particles.Current)
                {
                    particle.SetScalar(outputField, replacement);
                }

                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var current = particle.GetScalar(outputField);

                if (setMethod == ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE)
                {
                    particle.SetScalar(outputField, Math.Clamp(current + (blend * frameTime * input), min, max));
                    continue;
                }

                var initial = particle.GetInitialScalar(particles, outputField);

                var target = setMethod switch
                {
                    ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE => input * initial,
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE => initial + input,
                    ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => input * current,
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => current + input,
                    _ => input,
                };

                particle.SetScalar(outputField, Math.Clamp(float.Lerp(current, target, blend), min, max));
            }
        }
    }
}
