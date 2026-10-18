using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Provides a scalar float value that may vary per particle or per frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/CParticleFloatInput">CParticleFloatInput</seealso>
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

    /// <summary>
    /// An input evaluated for the whole collection, as a <c>CParticleCollectionFloatInput</c> is. A read
    /// for a particle gets the collection value, which holds for the whole function call.
    /// </summary>
    readonly struct CollectionNumberInput : INumberProvider
    {
        private readonly INumberProvider input;

        public CollectionNumberInput(INumberProvider input)
        {
            this.input = input;
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => input.NextNumber(renderState);

        public float NextNumber(ParticleSystemState renderState) => input.NextNumber(renderState);
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

        private readonly ParticleRandom.InputSeed inputSeed = new();

        private ParticleRandom.EvaluationKey? varyingKey;
        private float varyingDraw;
        private float varyingSignDraw;

        /// <param name="parse">The input block.</param>
        /// <param name="isBiased">Whether the draw goes through a bias curve.</param>
        /// <param name="defaultMode">The random mode when the block omits it.</param>
        public RandomNumberProvider(ParticleDefinitionParser parse, bool isBiased = false,
            ParticleFloatRandomMode defaultMode = ParticleFloatRandomMode.PF_RANDOM_MODE_CONSTANT)
        {
            minRange = parse.Float("m_flRandomMin");
            maxRange = parse.Float("m_flRandomMax", 1f);
            hasRandomSignFlip = parse.Boolean("m_bHasRandomSignFlip", hasRandomSignFlip);
            randomMode = parse.Enum("m_nRandomMode", defaultMode);

            this.isBiased = isBiased;

            if (isBiased)
            {
                biasParam = parse.Float("m_flBiasParameter");
                biasType = parse.Enum<ParticleFloatBiasType>("m_nBiasType", biasType);
            }
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => Evaluate(renderState.Random, particle.Index, particle.ParticleId);

        public float NextNumber(ParticleSystemState renderState)
            => Evaluate(renderState.Random, -1, 0);

        /// <summary>
        /// A constant input reads the slot keyed by its own seed and the particle id. A varying input
        /// draws once per block of four particles in a function call, or once per call for the collection.
        /// </summary>
        private float Evaluate(ParticleRandom random, int particleIndex, int particleId)
        {
            float draw;
            var sign = 1f;

            if (randomMode == ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING)
            {
                var key = random.Evaluation(particleIndex);

                if (varyingKey != key)
                {
                    varyingKey = key;
                    varyingDraw = random.Next();
                    varyingSignDraw = hasRandomSignFlip ? random.Next() : 1f;
                }

                draw = varyingDraw;
                sign = varyingSignDraw;
            }
            else
            {
                var seed = inputSeed.Get(random);
                draw = random.ForParticle(particleId, seed);

                if (hasRandomSignFlip)
                {
                    sign = random.ForParticle(particleId, seed + SignFlipOffset);
                }
            }

            if (isBiased)
            {
                draw = ParticleMath.BiasFromParameter(MathF.Min(1f, draw), biasParam, biasType);
            }

            var value = MathUtils.Clamp(MathUtils.Lerp(minRange, maxRange, draw), minRange, maxRange);

            return sign < 0.5f ? -value : value;
        }
    }

    /// <summary>
    /// PF_TYPE_NAMED_VALUE. The system's current float named value through the mapping, or the mapping
    /// of 0 while neither a local default nor a host has given the name one.
    /// </summary>
    class NamedValueNumberProvider : INumberProvider
    {
        private readonly string name;
        private readonly AttributeMapping attributeMapping;

        public NamedValueNumberProvider(ParticleDefinitionParser parse)
        {
            name = parse.String("m_NamedValue");
            attributeMapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            if (renderState.Data?.NamedValues is not { } namedValues || !namedValues.TryGetFloat(name, out var value))
            {
                value = 0f;
            }

            return attributeMapping.ApplyMapping(value);
        }
    }

    // Collection Age
    class CollectionAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public CollectionAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(renderState.Age);

        /// <summary>Evaluates the input as if the system were <paramref name="age"/> seconds old.</summary>
        public float AtAge(float age) => attributeMapping.ApplyMapping(age);
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
        private readonly float[] tiers;

        public DetailLevelNumberProvider(ParticleDefinitionParser parse)
        {
            tiers = [parse.Float("m_flLOD0"), parse.Float("m_flLOD1"), parse.Float("m_flLOD2"), parse.Float("m_flLOD3")];
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

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => attributeMapping.ApplyMapping(particle.Lifetime <= 0f ? 0f : particle.Age / particle.Lifetime);

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>PF_TYPE_PARTICLE_FLOAT. A scalar attribute, with angles read in degrees.</summary>
    class PerParticleNumberProvider : INumberProvider
    {
        private readonly ParticleField field = ParticleField.Radius;

        private readonly AttributeMapping mapping;

        public PerParticleNumberProvider(ParticleDefinitionParser parse)
        {
            field = parse.ParticleField("m_nScalarAttribute", field);
            mapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => mapping.ApplyMapping(Read(ref particle, field));

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public static float Read(ref Particle particle, ParticleField field)
        {
            var value = particle.GetScalar(field);
            return field.IsAngleField() ? float.RadiansToDegrees(value) : value;
        }
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_INITIAL_FLOAT. A scalar attribute as the particle's initializers left it, read
    /// as <see cref="PerParticleNumberProvider"/> does.
    /// </summary>
    class PerParticleInitialNumberProvider : INumberProvider
    {
        private readonly ParticleField field = ParticleField.Radius;

        private readonly AttributeMapping mapping;

        public PerParticleInitialNumberProvider(ParticleDefinitionParser parse)
        {
            field = parse.ParticleField("m_nScalarAttribute", field);
            mapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => mapping.ApplyMapping(PerParticleNumberProvider.Read(ref renderState.InitialOf(ref particle), field));

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_VECTOR_COMPONENT and PF_TYPE_PARTICLE_INITIAL_VECTOR_COMPONENT. One component of
    /// a vector attribute, the initial variant as the particle's initializers left it.
    /// </summary>
    class PerParticleVectorComponentNumberProvider : INumberProvider
    {
        private readonly ParticleField field = ParticleField.Color;
        private readonly int component;
        private readonly bool initial;

        private readonly AttributeMapping mapping;

        public PerParticleVectorComponentNumberProvider(ParticleDefinitionParser parse, bool initial)
        {
            field = parse.ParticleField("m_nVectorAttribute", field);
            component = parse.Int32("m_nVectorComponent");
            this.initial = initial;
            mapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            ref var source = ref initial ? ref renderState.InitialOf(ref particle) : ref particle;
            return mapping.ApplyMapping(source.GetVectorComponent(field, component));
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Speed
    class PerParticleSpeedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleSpeedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => attributeMapping.ApplyMapping(particle.GetVelocity(renderState.Data?.CurrentFrameTime ?? 0f).Length());
        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_NUMBER. The particle's place in the collection, counted back from the particle
    /// count when the order is reversed. Initializers instead read how many particles the system had
    /// created before this one.
    /// </summary>
    class PerParticleCountNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly bool reverseOrder;

        public PerParticleCountNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            reverseOrder = parse.Boolean("m_bReverseOrder");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var number = reverseOrder ? renderState.ParticleCount - particle.Index
                : renderState.InitializingParticles ? particle.UniqueParticleId
                : particle.Index;

            return attributeMapping.ApplyMapping(number);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    // Particle Count Percent of Total Count (0-1)
    class PerParticleCountNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var divisor = renderState.Data?.BehaviorVersion >= 12
                ? Math.Max(renderState.ParticleCount - 1, 1)
                : Math.Max(renderState.ParticleCount, 1);
            return attributeMapping.ApplyMapping(particle.Index * (1f / divisor));
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_ROPE_SEGMENT. The segment position held in X of <see cref="Particle.RopeSegmentData"/>,
    /// or its distance to the segment count in Z when the order is reversed.
    /// </summary>
    class RopeSegmentNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        private readonly bool reverseOrder;

        public RopeSegmentNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            reverseOrder = parse.Boolean("m_bReverseOrder");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var data = particle.RopeSegmentData;
            return attributeMapping.ApplyMapping(reverseOrder ? data.Z - data.X : data.X);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;
    }

    /// <summary>PF_TYPE_PARTICLE_ROPE_SEGMENT_NORMALIZED. Y of <see cref="Particle.RopeSegmentData"/>.</summary>
    class RopeSegmentNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;

        public RopeSegmentNormalizedNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState) => attributeMapping.ApplyMapping(particle.RopeSegmentData.Y);

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
    /// lives in <see cref="NoiseEvaluator"/>.
    /// </summary>
    class NoiseNumberProvider : INumberProvider
    {
        private readonly ParticleField inputField = ParticleField.Position;
        private readonly int controlPoint;
        private readonly NoiseEvaluator noise;
        private readonly AttributeMapping mapping;

        public NoiseNumberProvider(ParticleDefinitionParser parse)
        {
            inputField = parse.ParticleField("m_nNoiseInputVectorAttribute", inputField);
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
            noise = new NoiseEvaluator(parse);
            mapping = new AttributeMapping(parse);
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
            => mapping.ApplyMapping(noise.Evaluate(particle.GetVector(inputField), renderState.Age));

        public float NextNumber(ParticleSystemState renderState)
            => mapping.ApplyMapping(noise.Evaluate(
                controlPoint < 0 ? Vector3.Zero : renderState.GetControlPoint(controlPoint).Position,
                renderState.Age));
    }

    /// <summary>
    /// PF_TYPE_CONTROL_POINT_SPEED. How far the control point moved since the previous frame, over the
    /// simulation step being run, so a frame split into several steps reads several times the speed.
    /// </summary>
    class ControlPointSpeedNumberProvider : INumberProvider
    {
        private const float FallbackStep = 0.1f;

        private readonly AttributeMapping attributeMapping;
        private readonly int cp;

        public ControlPointSpeedNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
            cp = parse.Int32("m_nControlPoint");
        }

        public float NextNumber(ref Particle particle, ParticleSystemState renderState)
        {
            var point = renderState.GetControlPoint(cp);
            var step = renderState.Data?.CurrentFrameTime ?? 0f;

            if (step == 0f)
            {
                step = FallbackStep;
            }

            return attributeMapping.ApplyMapping(Vector3.Distance(point.Position, point.PositionPrevious) / step);
        }
    }

    /// <summary>
    /// PF_TYPE_CONTROL_POINT_CHANGE_AGE. How long before the reading system's current time the control
    /// point last changed, see <see cref="ControlPoint.ChangeTime"/>.
    /// </summary>
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
            => attributeMapping.ApplyMapping(renderState.Age - renderState.GetControlPoint(cp).ChangeTime);
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
}
