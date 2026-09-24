using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Provides a scalar float value that may vary per particle or per frame.
    /// </summary>
    internal interface INumberProvider
    {
        /// <summary>
        /// Returns the next number value, evaluated per particle.
        /// </summary>
        float NextNumber(ref Particle particle, ParticleSystemState renderState);

        /// <summary>
        /// ONLY use this in emitters and renderers, where per-particle values can't be accessed. Otherwise, use the other version.
        /// </summary>
        public float NextNumber()
            => NextNumber(ParticleSystemState.Default);

        /// <summary>
        /// Returns the next number value using system-level state only. Inputs that read a particle
        /// return 0 here.
        /// </summary>
        public float NextNumber(ParticleSystemState renderState)
            => NextNumber(ref Particle.Default, renderState);

        /// <summary>
        /// Returns the next number value truncated to an integer.
        /// </summary>
        public int NextInt(ref Particle particle, ParticleSystemState renderState)
            => (int)NextNumber(ref particle, renderState);
    }

    // Literal Number
    readonly struct LiteralNumberProvider : INumberProvider
    {
        private readonly float value;

        /// <summary>The constant this provider returns, for callers that specialise on known values.</summary>
        public float Value => value;

        public LiteralNumberProvider(float value)
        {
            this.value = value;
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => value;
    }

    // Random Uniform/Random Biased
    class RandomNumberProvider : INumberProvider
    {
        /// <summary>Displacement between the value draw and the draw that decides the sign.</summary>
        private const int SignFlipOffset = 37;

        private readonly float minRange;
        private readonly float maxRange;
        private readonly ParticleFloatRandomMode randomMode;

        private readonly bool isBiased;
        private readonly ParticleFloatBiasType biasType = ParticleFloatBiasType.PF_BIAS_TYPE_STANDARD;
        private readonly float biasParam;

        private readonly bool hasRandomSignFlip;

        private readonly int sampleOffset;

        /// <param name="parse">The input block.</param>
        /// <param name="isBiased">Whether the draw goes through a bias curve.</param>
        /// <param name="defaultMax">The upper end of the range when the block omits it.</param>
        /// <param name="defaultMode">The random mode when the block omits it.</param>
        public RandomNumberProvider(ParticleDefinitionParser parse, bool isBiased = false, float defaultMax = 0f,
            ParticleFloatRandomMode defaultMode = ParticleFloatRandomMode.PF_RANDOM_MODE_CONSTANT)
        {
            minRange = parse.Float("m_flRandomMin");
            maxRange = parse.Float("m_flRandomMax", defaultMax);
            hasRandomSignFlip = parse.Boolean("m_bHasRandomSignFlip", hasRandomSignFlip);
            randomMode = parse.Enum("m_nRandomMode", defaultMode);

            this.isBiased = isBiased;

            if (isBiased)
            {
                biasParam = parse.Float("m_flBiasParameter");
                biasType = parse.Enum<ParticleFloatBiasType>("m_nBiasType", biasType);
            }

            // Only an input that is constant per particle needs its own slot; a varying one draws from
            // the running counter and is already separated from its siblings.
            if (randomMode == ParticleFloatRandomMode.PF_RANDOM_MODE_CONSTANT)
            {
                sampleOffset = parse.NextInputOrdinal();
            }
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var varying = randomMode == ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING;

            var random = varying
                ? renderState.Random.Next()
                : renderState.Random.ForParticle(particle.ParticleId, sampleOffset);

            if (isBiased)
            {
                random = ParticleMath.BiasFromParameter(random, biasParam, biasType);
            }

            var value = float.Lerp(minRange, maxRange, random);

            if (hasRandomSignFlip)
            {
                var sign = varying
                    ? renderState.Random.Next()
                    : renderState.Random.ForParticle(particle.ParticleId, sampleOffset + SignFlipOffset);

                if (sign < 0.5f)
                {
                    value = -value;
                }
            }

            return value;
        }
    }

    // Collection Age
    class CollectionAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public CollectionAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(renderState.Age);
    }

    // How long the endcap has been running, 0 outside it
    class EndCapAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public EndCapAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(renderState.EndCapAge);
    }

    class DetailLevelNumberProvider : INumberProvider
    {
        private readonly float[] tiers = new float[4];

        public DetailLevelNumberProvider(ParticleDefinitionParser parse)
        {
            // Absent tiers fall back to the nearest lower authored tier.
            var value = parse.Float("m_flLOD0");
            tiers[0] = value;

            var tier = 1;
            foreach (var key in (ReadOnlySpan<string>)["m_flLOD1", "m_flLOD2", "m_flLOD3"])
            {
                if (parse.Data.ContainsKey(key))
                {
                    value = parse.Float(key);
                }

                tiers[tier++] = value;
            }
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            return tiers[Math.Clamp((int)renderState.DetailLevel, 0, tiers.Length - 1)];
        }
    }

    // Particle Age
    class ParticleAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(particle.Age);
        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Age (0-1)
    class ParticleAgeNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(particle.NormalizedAge);
        public float NextNumber(ParticleSystemState renderState) => 0f;
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
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => mapping.ApplyMapping(particle.GetScalar(field));
        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_INITIAL_FLOAT. Reads the particle's current attribute value, which equals
    /// the initial value for attributes that never mutate after spawn (creation time, particle id);
    /// for attributes operators rewrite it is an approximation.
    /// </summary>
    class PerParticleInitialNumberProvider : INumberProvider
    {
        private readonly ParticleField field;

        private readonly AttributeMapping mapping;

        public PerParticleInitialNumberProvider(ParticleDefinitionParser parse)
        {
            field = parse.ParticleField("m_nScalarAttribute");
            mapping = new AttributeMapping(parse);
        }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => mapping.ApplyMapping(particle.GetScalar(field));
        public float NextNumber(ParticleSystemState renderState) => 0f;
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
        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            return mapping.ApplyMapping(particle.GetVectorComponent(field, component));
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Speed
    class PerParticleSpeedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleSpeedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(particle.Speed);
        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Count
    class PerParticleCountNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(particle.UniqueParticleId);
        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Count Percent of Total Count (0-1)
    class PerParticleCountNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            // Mapping input ranges for this provider type are authored in normalized 0-1 space.
            // Index is the slot in the alive list; UniqueParticleId is a lifetime spawn counter and would exceed the count.
            // From behavior version 12 the last live particle reads 1 instead of count/(count+1).
            var divisor = renderState.Data?.BehaviorVersion >= 12
                ? Math.Max(renderState.ParticleCount - 1, 1)
                : Math.Max(renderState.ParticleCount, 1);
            return attributeMapping.ApplyMapping(particle.Index / (float)divisor);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Control Point Component
    class ControlPointComponentNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;
        private readonly int vectorComponent;

        public ControlPointComponentNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
            vectorComponent = parse.Int32("m_nVectorComponent");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            // Control points carry raw switches and scalars that the mapping turns into the value the
            // effect actually wants - an unset point commonly has to read as a multiplier of 1, not 0.
            return attributeMapping.ApplyMapping(renderState.GetControlPoint(cp).Position.GetComponent(vectorComponent));
        }
    }

    /// <summary>
    /// PF_TYPE_RENDERER_CAMERA_DISTANCE. Distance from the render camera to the control point, or to
    /// the centre of the system's bounds, shared by every particle.
    /// </summary>
    class RendererCameraDistanceNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;
        private readonly bool useBoundsCenter;

        public RendererCameraDistanceNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
            useBoundsCenter = parse.Boolean("m_bUseBoundsCenter");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var target = useBoundsCenter && renderState.Data is { } system
                ? system.LocalBoundingBox.Center + system.MainControlPoint.Position
                : renderState.GetControlPoint(cp).Position;

            return attributeMapping.ApplyMapping(Vector3.Distance(renderState.CameraPosition, target));
        }
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_NOISE. Fractal noise over a particle vector attribute, or over the live
    /// position of the input's control point when evaluated at collection level (emitters,
    /// renderers); a negative control point index samples the origin. The evaluation pipeline
    /// lives in <see cref="Utils.NoiseEvaluator"/>.
    /// </summary>
    class NoiseNumberProvider : INumberProvider
    {
        private readonly ParticleField inputField = ParticleField.Position;
        private readonly int controlPoint;
        private readonly Utils.NoiseEvaluator noise;
        private readonly AttributeMapping mapping;

        public NoiseNumberProvider(ParticleDefinitionParser parse)
        {
            inputField = parse.ParticleField("m_nNoiseInputVectorAttribute", inputField);
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
            noise = new Utils.NoiseEvaluator(parse);
            mapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => mapping.ApplyMapping(noise.Evaluate(particle.GetVector(inputField), renderState.Age));

        public float NextNumber(ParticleSystemState renderState)
            => mapping.ApplyMapping(noise.Evaluate(
                controlPoint < 0 ? Vector3.Zero : renderState.GetControlPoint(controlPoint).Position,
                renderState.Age));
    }

    // Control Point Speed
    class ControlPointSpeedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;

        public ControlPointSpeedNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var speed = renderState.GetControlPoint(cp).Velocity.Length();
            return attributeMapping.ApplyMapping(speed);
        }
    }

    /// <summary>PF_TYPE_CONTROL_POINT_CHANGE_AGE. How long ago the control point last changed, see <see cref="ControlPoint.ChangeAge"/>.</summary>
    class ControlPointChangeAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;

        public ControlPointChangeAgeNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => attributeMapping.ApplyMapping(renderState.GetControlPoint(cp).ChangeAge);
    }

    /// <summary>PF_TYPE_CONTROL_POINT_IS_SET. 1 once the control point has changed, see <see cref="ControlPoint.ChangeTime"/>, and 0 before that.</summary>
    class ControlPointIsSetNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;

        public ControlPointIsSetNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var isSet = cp != -1 && renderState.GetControlPoint(cp).ChangeTime != -1f;
            return attributeMapping.ApplyMapping(isSet ? 1f : 0f);
        }
    }

    /// <summary>
    /// PF_TYPE_SNAPSHOT_COUNT. How many particles the snapshot on the control point holds, 0 when it
    /// carries none or a subset is authored.
    /// </summary>
    class SnapshotCountNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly SnapshotBinding snapshot;

        public SnapshotCountNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            snapshot = new SnapshotBinding(parse, "m_nControlPoint", 0);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => attributeMapping.ApplyMapping(snapshot.Count(renderState));
    }

    // Distance from the particle to a control point
    class ControlPointDistanceNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly int cp;

        public ControlPointDistanceNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var distance = Vector3.Distance(particle.Position, renderState.GetControlPoint(cp).Position);
            return attributeMapping.ApplyMapping(distance);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /* Unaccounted for params:
     * m_NamedValue
     */
}
