using System;
using System.Collections.Generic;
using System.Globalization;
using GUI.Types.ParticleRenderer.Utils;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    interface INumberProvider
    {
        float NextNumber(ref Particle particle, ParticleSystemRenderState renderState);

        /// <summary>
        /// ONLY use this in emitters and renderers, where per-particle values can't be accessed. Otherwise, use the other version.
        /// </summary>
        /// <param name="numberProvider"></param>
        /// <returns></returns>
        public float NextNumber()
            => NextNumber(ref Particle.Default, ParticleSystemRenderState.Default);

        public float NextNumber(ParticleSystemRenderState renderState)
            => NextNumber(ref Particle.Default, renderState);

        public int NextInt(ref Particle particle, ParticleSystemRenderState renderState)
            => (int)NextNumber(ref particle, renderState);
    }

    // Literal Number
    class LiteralNumberProvider : INumberProvider
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
        private readonly PfRandomMode randomMode;

        private readonly bool isBiased;
        private readonly PfBiasType biasType = PfBiasType.Standard;
        private readonly float biasParam;

        public RandomNumberProvider(IKeyValueCollection keyValues, bool isBiased = false)
        {
            minRange = keyValues.GetFloatProperty("m_flRandomMin");
            maxRange = keyValues.GetFloatProperty("m_flRandomMax");

            // Should it be checking behavior version?
            if (keyValues.GetProperty<string>("m_nType") != keyValues.GetProperty<string>("m_nRandomMode"))
            {
                randomMode = keyValues.GetEnumValue<PfRandomMode>("m_nRandomMode", normalize: true);
            }

            this.isBiased = isBiased;

            if (isBiased)
            {
                biasParam = keyValues.GetFloatProperty("m_flBiasParameter");
                if (keyValues.ContainsKey("m_nBiasType"))
                {
                    biasType = keyValues.GetEnumValue<PfBiasType>("m_nBiasType", normalize: true);
                }
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

            return MathUtils.Lerp(random, minRange, maxRange);
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
        private readonly float lod1;
        private readonly float lod2;
        private readonly float lod3;

        public DetailLevelNumberProvider(IKeyValueCollection keyValues)
        {
            lod0 = keyValues.GetFloatProperty("m_flLOD0");
            lod1 = keyValues.GetFloatProperty("m_flLOD1");
            lod2 = keyValues.GetFloatProperty("m_flLOD2");
            lod3 = keyValues.GetFloatProperty("m_flLOD3");
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
        public ParticleAgeNumberProvider(IKeyValueCollection keyValues) { attributeMapping = new AttributeMapping(keyValues); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.Age);
    }

    // Particle Age (0-1)
    class ParticleAgeNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNormalizedNumberProvider(IKeyValueCollection keyValues) { attributeMapping = new AttributeMapping(keyValues); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.NormalizedAge);
    }

    // Particle Float
    // Note that the per-particle parameters are not useable in intializers, so we don't need to account for that somehow
    class PerParticleNumberProvider : INumberProvider
    {
        private readonly ParticleField field;

        private readonly AttributeMapping mapping;

        public PerParticleNumberProvider(IKeyValueCollection parameters)
        {
            field = (ParticleField)parameters.GetIntegerProperty("m_nScalarAttribute");
            mapping = new AttributeMapping(parameters);
        }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => mapping.ApplyMapping(particle.GetScalar(field));
    }

    // Particle Vector Component
    class PerParticleVectorComponentNumberProvider : INumberProvider
    {
        private readonly ParticleField field;
        private readonly int component;

        private readonly AttributeMapping mapping;

        public PerParticleVectorComponentNumberProvider(IKeyValueCollection parameters)
        {
            field = (ParticleField)parameters.GetIntegerProperty("m_nVectorAttribute");
            component = parameters.GetInt32Property("m_nVectorComponent");
            mapping = new AttributeMapping(parameters);
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
        public PerParticleSpeedNumberProvider(IKeyValueCollection keyValues) { attributeMapping = new AttributeMapping(keyValues); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.Speed);
    }

    // Particle Count
    class PerParticleCountNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNumberProvider(IKeyValueCollection keyValues) { attributeMapping = new AttributeMapping(keyValues); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState) => attributeMapping.ApplyMapping(particle.ParticleCount);
    }

    // Particle Count Percent of Total Count (0-1)
    class PerParticleCountNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNormalizedNumberProvider(IKeyValueCollection keyValues) { attributeMapping = new AttributeMapping(keyValues); }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return attributeMapping.ApplyMapping(particle.ParticleCount) / Math.Max(renderState.ParticleCount, 1);
        }
    }

    // Control Point Component
    class ControlPointComponentNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;
        private readonly int vectorComponent;
        public ControlPointComponentNumberProvider(IKeyValueCollection keyValues)
        {
            attributeMapping = new AttributeMapping(keyValues);
            cp = keyValues.GetInt32Property("m_nControlPoint");
            vectorComponent = keyValues.GetInt32Property("m_nVectorComponent");
        }
        public float NextNumber(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return renderState.GetControlPoint(cp).Position.GetComponent(vectorComponent);
        }
    }

    static class INumberProviderExtensions
    {
        public static INumberProvider GetNumberProvider(this IKeyValueCollection keyValues, string propertyName)
        {
            var property = keyValues.GetProperty<object>(propertyName);

            if (property is IKeyValueCollection numberProviderParameters)
            {
                var type = numberProviderParameters.GetProperty<string>("m_nType");
                switch (type)
                {
                    case "PF_TYPE_LITERAL":
                        return new LiteralNumberProvider(numberProviderParameters.GetFloatProperty("m_flLiteralValue"));
                    case "PF_TYPE_RANDOM_UNIFORM":
                        return new RandomNumberProvider(numberProviderParameters, false);
                    case "PF_TYPE_RANDOM_BIASED":
                        return new RandomNumberProvider(numberProviderParameters, true);
                    case "PF_TYPE_COLLECTION_AGE":
                        return new CollectionAgeNumberProvider();
                    case "PF_TYPE_CONTROL_POINT_COMPONENT":
                        return new ControlPointComponentNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_DETAIL_LEVEL":
                        return new DetailLevelNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_AGE":
                        return new ParticleAgeNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_AGE_NORMALIZED":
                        return new ParticleAgeNormalizedNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_FLOAT":
                        return new PerParticleNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_VECTOR_COMPONENT":
                        return new PerParticleVectorComponentNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_SPEED":
                        return new PerParticleSpeedNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_NUMBER":
                        return new PerParticleCountNumberProvider(numberProviderParameters);
                    case "PF_TYPE_PARTICLE_NUMBER_NORMALIZED":
                        return new PerParticleCountNormalizedNumberProvider(numberProviderParameters);
                    // KNOWN TYPES WE DON'T SUPPORT:
                    // PF_TYPE_ENDCAP_AGE - unsupported because we don't support endcaps
                    // PF_TYPE_CONTROL_POINT_COMPONENT - todo?
                    // PF_TYPE_CONTROL_POINT_CHANGE_AGE - no way.
                    // PF_TYPE_CONTROL_POINT_SPEED - new in cs2? def not going to support this
                    // PF_TYPE_PARTICLE_NOISE - exists only in deskjob and CS2. Likely added in behavior version 11 or 12.
                    // PF_TYPE_NAMED_VALUE - seen in dota's particle.dll?? not in deskjob's, so in behavior version 13+?
                    default:
                        if (numberProviderParameters.ContainsKey("m_flLiteralValue"))
                        {
                            Console.Error.WriteLine($"Number provider of type {type} is not directly supported, but it has m_flLiteralValue.");
                            return new LiteralNumberProvider(numberProviderParameters.GetFloatProperty("m_flLiteralValue"));
                        }

                        throw new InvalidCastException($"Could not create number provider of type {type}.");
                }
            }
            else
            {
                return new LiteralNumberProvider((float)Convert.ToDouble(property, CultureInfo.InvariantCulture));
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
}
