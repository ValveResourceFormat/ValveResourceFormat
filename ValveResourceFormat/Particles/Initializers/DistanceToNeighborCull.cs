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
            ? ParticleField.LifeDuration.Mask()
            : ParticleField.LifeDuration.Mask() | fieldModify.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var neighbors = particles.Current;

            // The inputs are evaluated for every live particle, and each particle of the run reads its own value
            // rather than the value of the particle as far into the collection as it is into the run.
            // The modify value is read even further off: the n-th particle to find a neighbour reads the value of particle n
            var ranges = distance.Fill(run, neighbors, particleSystemState);
            var overlaps = lifespanOverlap.Fill(run, neighbors, particleSystemState);
            var modifyValues = modify.Fill(run, neighbors, particleSystemState);

            var lifetimes = neighbors.Floats(ParticleField.LifeDuration);
            var creationTimes = neighbors.ReadFloats(ParticleField.CreationTime);
            var positions = neighbors.ReadVectors(ParticleField.Position);
            var radii = includeRadii ? neighbors.ReadFloats(ParticleField.Radius) : default;
            var modifiedFields = fieldModify != ParticleField.NoneDisabled && ParticleBuffer.IsScalar(fieldModify)
                ? neighbors.Floats(fieldModify)
                : default;

            for (var i = 0; i < run.Length; i++)
            {
                var index = run.Start + i;
                var range = ranges[i];
                var overlap = 1f - overlaps[i];
                var modifyValue = modifyValues[i];

                if (lifetimes[index] <= 0f)
                {
                    continue;
                }

                for (var j = neighbors.Length - 1; j >= 0; j--)
                {
                    if (j == index)
                    {
                        continue;
                    }

                    if (lifetimes[j] <= 0f)
                    {
                        continue;
                    }

                    if (overlap < 1f && (particleSystemState.Age - creationTimes[j]) / lifetimes[j] > overlap)
                    {
                        continue;
                    }

                    var radiusSum = includeRadii ? radii[j] + radii[index] : 0f;

                    if (range * range <= Vector3.DistanceSquared(positions[index], positions[j]) - (radiusSum * radiusSum))
                    {
                        continue;
                    }

                    if (fieldModify == ParticleField.NoneDisabled)
                    {
                        lifetimes[index] = -1f;
                    }
                    else if (!modifiedFields.IsEmpty)
                    {
                        var current = modifiedFields[useNeighbor ? j : index];
                        modifiedFields[index] = setMethod switch
                        {
                            ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => modifyValue * current,
                            ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => modifyValue + current,
                            ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => (modifyValue * particleSystemState.CurrentStep) + current,
                            _ => modifyValue,
                        };
                    }

                    break;
                }
            }
        }
    }
}
