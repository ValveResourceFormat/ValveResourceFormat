using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles
{
    interface INumberProvider
    {
        float NextNumber(ref Particle particle, ParticleSystemRenderState renderState);

        /// <summary>
        /// ONLY use this in emitters and renderers, where per-particle values can't be accessed. Otherwise, use the other version.
        /// </summary>
        public float NextNumber()
            => NextNumber(ref Particle.Default, ParticleSystemRenderState.Default);

        public float NextNumber(ParticleSystemRenderState renderState)
            => NextNumber(ref Particle.Default, renderState);

        public int NextInt(ref Particle particle, ParticleSystemRenderState renderState)
            => (int)NextNumber(ref particle, renderState);
    }

    // Literal Number
    readonly struct LiteralNumberProvider : INumberProvider
    {
        private readonly float value;

        public LiteralNumberProvider(float value)
        {
            this.value = value;
        }

        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => value;
    }

    // Random Uniform/Random Biased
    class RandomNumberProvider : INumberProvider
    {
        private readonly float minRange;
        private readonly float maxRange;
        private readonly ParticleFloatRandomMode randomMode;

        private readonly bool isBiased;
        private readonly ParticleFloatBiasType biasType = ParticleFloatBiasType.PF_BIAS_TYPE_STANDARD;
        private readonly float biasParam;

        private readonly bool hasRandomSignFlip;

        public RandomNumberProvider(ParticleDefinitionParser parse, bool isBiased = false)
        {
            minRange = parse.Float("m_flRandomMin");
            maxRange = parse.Float("m_flRandomMax");
            hasRandomSignFlip = parse.Boolean("m_bHasRandomSignFlip", hasRandomSignFlip);

            // Should it be checking behavior version?
            if (parse.Data.GetProperty<string>("m_nType") != parse.Data.GetProperty<string>("m_nRandomMode"))
            {
                randomMode = parse.Enum<ParticleFloatRandomMode>("m_nRandomMode", randomMode);
            }

            this.isBiased = isBiased;

            if (isBiased)
            {
                biasParam = parse.Float("m_flBiasParameter");
                biasType = parse.Enum<ParticleFloatBiasType>("m_nBiasType", biasType);
            }
        }

        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var random = Random.Shared.NextSingle();

            // currently does nothing as it's unclear how it's done
            if (isBiased)
            {
                random = NumericBias.ApplyBias(random, biasParam, biasType);
            }

            var value = float.Lerp(minRange, maxRange, random);

            if (hasRandomSignFlip && Random.Shared.Next(0, 2) == 0) // 50% chance to flip sign
            {
                value *= -1f;
            }

            return value;
        }
    }

    // Collection Age
    class CollectionAgeNumberProvider : INumberProvider
    {
        public CollectionAgeNumberProvider() { }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => renderState.Age;
    }

    class DetailLevelNumberProvider : INumberProvider
    {
        private readonly float lod0;
#if false
        private readonly float lod1;
        private readonly float lod2;
        private readonly float lod3;
#endif

        public DetailLevelNumberProvider(ParticleDefinitionParser parse)
        {
            lod0 = parse.Float("m_flLOD0");
#if false
            lod1 = parse.Float("m_flLOD1");
            lod2 = parse.Float("m_flLOD2");
            lod3 = parse.Float("m_flLOD3");
#endif
        }

        // Just assume detail level is Ultra
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return lod0;
        }
    }

    // Particle Age
    class ParticleAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.Age);
    }

    // Particle Age (0-1)
    class ParticleAgeNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.NormalizedAge);
    }

    // Particle Float
    // Note that the per-particle parameters are not usable in initializers, so we don't need to account for that somehow
    class PerParticleNumberProvider : INumberProvider
    {
        private readonly ParticleField field;

        private readonly AttributeMapping mapping;

        public PerParticleNumberProvider(ParticleDefinitionParser parse)
        {
            field = parse.ParticleField("m_nScalarAttribute");
            mapping = new AttributeMapping(parse);
        }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => mapping.ApplyMapping(particle.GetScalar(field));
    }

    // Particle Vector Component
    class PerParticleVectorComponentNumberProvider : INumberProvider
    {
        private readonly ParticleField field;
        private readonly int component;

        private readonly AttributeMapping mapping;

        public PerParticleVectorComponentNumberProvider(ParticleDefinitionParser parse)
        {
            field = parse.ParticleField("m_nVectorAttribute");
            component = parse.Int32("m_nVectorComponent");
            mapping = new AttributeMapping(parse);
        }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return mapping.ApplyMapping(particle.GetVectorComponent(field, component));
        }
    }

    // Particle Speed
    class PerParticleSpeedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleSpeedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.Speed);
    }

    // Particle Count
    class PerParticleCountNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.ParticleID);
    }

    // Particle Count Percent of Total Count (0-1)
    class PerParticleCountNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return attributeMapping.ApplyMapping(particle.ParticleID) / Math.Max(renderState.ParticleCount, 1);
        }
    }

    // Control Point Component
    class ControlPointComponentNumberProvider : INumberProvider
    {
        //private readonly AttributeMapping attributeMapping;
        private readonly int cp;
        private readonly int vectorComponent;

        public ControlPointComponentNumberProvider(ParticleDefinitionParser parse)
        {
            //attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
            vectorComponent = parse.Int32("m_nVectorComponent");
        }

        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position.GetComponent(vectorComponent);
        }
    }

    /* Unaccounted for params:
     * m_NamedValue
     * m_flRandomMin
     * m_flRandomMax
     * m_bHasRandomSignFlip
     * m_nRandomMode
     */
}
