namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills each particle whose whole lifetime is shorter than the endcap has been running, so the
    /// collection drains over the spread of its authored lifetimes rather than at once. The threshold
    /// is offset by the collection age at which the endcap started, delaying the drain.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_EndCapDecay">C_OP_EndCapDecay</seealso>
    class EndCapDecay : ParticleFunctionOperator
    {
        public EndCapDecay(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            var endCapAge = particleSystemState.EndCapAge - particleSystemState.EndCapStartAge;

            var current = particles.Current;
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);

            for (var i = 0; i < current.Length; i++)
            {
                var lifetime = lifetimes[i];

                if (lifetime <= 0f || lifetime <= endCapAge)
                {
                    particles.Kill(i);
                }
            }
        }
    }
}
