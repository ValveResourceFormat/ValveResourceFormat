namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills a particle once its age reaches its lifetime.
    /// </summary>
    /// <remarks>
    /// "Lifespan Decay" in the particle editor. All effects should have a decay operator
    /// unless the particles are certain to be destroyed by some other means (usually code).
    /// With <c>m_bRopeDecay</c> a particle only dies together with the two particles after it, so a
    /// rope keeps its end segments until the whole tail has run out.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Decay">C_OP_Decay</seealso>
    class Decay : ParticleFunctionOperator
    {
        private readonly bool ropeDecay;
        private readonly bool forcePreserveParticleOrder;

        public Decay(ParticleDefinitionParser parse) : base(parse)
        {
            ropeDecay = parse.Boolean("m_bRopeDecay", ropeDecay);
            forcePreserveParticleOrder = parse.Boolean("m_bForcePreserveParticleOrder", forcePreserveParticleOrder);
        }

        public override bool IsOrderImportant => forcePreserveParticleOrder;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);

            if (!ropeDecay)
            {
                var creationTimes = current.ReadFloats(ParticleField.CreationTime);
                var time = current.Time;

                for (var i = 0; i < current.Length; i++)
                {
                    var lifetime = lifetimes[i];

                    if (lifetime <= 0f || lifetime <= time - creationTimes[i])
                    {
                        particles.Kill(i);
                    }
                }

                return;
            }

            var ages = current.ReadAges();
            var last = current.Length - 1;

            for (var i = 0; i <= last; i++)
            {
                if (ages[i] > lifetimes[i]
                    && ages[Math.Min(i + 1, last)] > lifetimes[Math.Min(i + 1, last)]
                    && ages[Math.Min(i + 2, last)] > lifetimes[Math.Min(i + 2, last)])
                {
                    particles.Kill(i);
                }
            }
        }
    }
}
