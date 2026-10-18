namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Culls a new particle when another live particle lies within a distance of it, measured between
    /// the radii when <c>m_bIncludeRadii</c> is set. With a modify field it instead writes a value to
    /// that attribute by a set method, applied to its own value or to the neighbour's.
    /// </summary>
    /// <remarks>
    /// The newest particles are searched first, the other new particles of the emission included, and
    /// the first neighbour found wins. A neighbour past <c>1 - m_flLifespanOverlap</c> of its life is
    /// skipped, as is any particle without a positive lifetime.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_DistanceToNeighborCull">C_INIT_DistanceToNeighborCull</seealso>
    class DistanceToNeighborCull : ParticleFunctionInitializer
    {
        private readonly INumberProvider distance = new LiteralNumberProvider(0f);
        private readonly bool includeRadii;
        private readonly INumberProvider lifespanOverlap = new LiteralNumberProvider(0f);
        private readonly ParticleField fieldModify = ParticleField.NoneDisabled;
        private readonly INumberProvider modify = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool useNeighbor;

        public DistanceToNeighborCull(ParticleDefinitionParser parse) : base(parse)
        {
            distance = parse.NumberProvider("m_flDistance", distance);
            includeRadii = parse.Boolean("m_bIncludeRadii", includeRadii);
            lifespanOverlap = parse.NumberProvider("m_flLifespanOverlap", lifespanOverlap);
            fieldModify = parse.ParticleField("m_nFieldModify", fieldModify);
            modify = parse.NumberProvider("m_flModify", modify);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            useNeighbor = parse.Boolean("m_bUseNeighbor", useNeighbor);
        }

        public override ulong WrittenFields => fieldModify == ParticleField.NoneDisabled
            ? FieldMask(ParticleField.LifeDuration)
            : FieldMask(ParticleField.LifeDuration) | FieldMask(fieldModify);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var range = distance.NextNumber(ref particle, particleSystemState);
            var overlap = 1f - lifespanOverlap.NextNumber(ref particle, particleSystemState);
            var modifyValue = modify.NextNumber(ref particle, particleSystemState);

            if (particle.Lifetime <= 0f)
            {
                return particle;
            }

            var neighbors = particles.Current;

            for (var i = neighbors.Length - 1; i >= 0; i--)
            {
                if (i == particle.Index)
                {
                    continue;
                }

                ref var neighbor = ref neighbors[i];

                if (neighbor.Lifetime <= 0f)
                {
                    continue;
                }

                if (overlap < 1f && (particleSystemState.Age - neighbor.CreationTime) / neighbor.Lifetime > overlap)
                {
                    continue;
                }

                var radii = includeRadii ? neighbor.Radius + particle.Radius : 0f;

                if (range * range <= Vector3.DistanceSquared(particle.Position, neighbor.Position) - (radii * radii))
                {
                    continue;
                }

                if (fieldModify == ParticleField.NoneDisabled)
                {
                    particle.Lifetime = -1f;
                }
                else
                {
                    var current = (useNeighbor ? ref neighbor : ref particle).GetScalar(fieldModify);
                    particle.SetScalar(fieldModify, setMethod switch
                    {
                        ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => modifyValue * current,
                        ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => modifyValue + current,
                        ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => (modifyValue * particles.CurrentFrameTime) + current,
                        _ => modifyValue,
                    });
                }

                break;
            }

            return particle;
        }
    }
}
