namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base class for particle operators that run each simulation frame to modify particle attributes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionOperator">CParticleFunctionOperator</seealso>
    abstract class ParticleFunctionOperator : ParticleFunction
    {
        protected ParticleFunctionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>Updates the particles of <paramref name="particles"/> for a step of <paramref name="frameTime"/> seconds, at <paramref name="strength"/>.</summary>
        public abstract void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength);

        /// <summary>
        /// Moves <paramref name="current"/> toward <paramref name="value"/> by <paramref name="strength"/>.
        /// A strength of 1 or more gives <paramref name="value"/> itself rather than overshooting it.
        /// </summary>
        protected static float BlendByStrength(float current, float value, float strength)
            => strength >= 1f ? value : (value * strength) + (current * (1f - strength));

        /// <inheritdoc cref="BlendByStrength(float, float, float)"/>
        protected static Vector3 BlendByStrength(Vector3 current, Vector3 value, float strength)
            => strength >= 1f ? value : (value * strength) + (current * (1f - strength));

        /// <summary>
        /// Records the initial value read of an operator that writes <paramref name="field"/> by
        /// <paramref name="method"/>: the field itself when the method combines with its initial value.
        /// </summary>
        protected static void AddInitialReadOf(ParticleDefinitionParser parse, ParticleSetMethod method, ParticleField field)
        {
            if (method.ReadsInitialValue())
            {
                parse.AddInitialRead(field);
            }
        }

        /// <summary>Kills a culled particle, raising the killed on cull event for the children that listen for it.</summary>
        protected static void KillCulled(ParticleCollection particles, int index, ParticleSystemState particleSystemState)
        {
            particles.Kill(index);

            if (particleSystemState.ListensFor(ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_CULL))
            {
                particles.RaiseEvent(index, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_CULL);
            }
        }
    }
}
