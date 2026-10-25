using System.Numerics.Tensors;
using System.Runtime.InteropServices;
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
        /// Returns the value for the whole collection, as emitters and renderers read it. Inputs that
        /// read a particle return 0 here.
        /// </summary>
        float NextNumber(ParticleSystemState renderState);

        /// <summary>
        /// Gets whether the input can give each particle its own value. One that cannot fills every
        /// particle with its collection value.
        /// </summary>
        bool IsPerParticle => false;

        /// <summary>
        /// Writes the value for each particle of <paramref name="particles"/> into
        /// <paramref name="values"/>, which holds one entry per particle. An input that reads no particle
        /// writes its collection value, and evaluates nothing for an empty span.
        /// </summary>
        void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            if (!particles.IsEmpty)
            {
                values.Fill(NextNumber(renderState));
            }
        }

        /// <summary>
        /// Evaluates the value for each particle of <paramref name="particles"/> as
        /// <see cref="Fill(ParticleSpan, ParticleSystemState, Span{float})"/> does, into a scratch column
        /// valid until the running function returns.
        /// </summary>
        Span<float> Fill(ParticleSpan particles, ParticleSystemState renderState)
        {
            var values = ParticleScratch.Floats(particles.Length);
            Fill(particles, renderState, values);
            return values;
        }

        /// <summary>
        /// Evaluates the input over <paramref name="evaluated"/>, which contains <paramref name="particles"/>,
        /// as a function that sets it up over those particles does, such as over every live particle.
        /// Returns the values of <paramref name="particles"/>, each of them reading its own value.
        /// </summary>
        Span<float> Fill(ParticleSpan particles, ParticleSpan evaluated, ParticleSystemState renderState)
            => Fill(evaluated, renderState).Slice(particles.Start - evaluated.Start, particles.Length);

        /// <summary>Returns the value <see cref="Fill(ParticleSpan, ParticleSystemState, Span{float})"/> writes for the particle at <paramref name="index"/> of <paramref name="particles"/>.</summary>
        public float NextNumber(ParticleSpan particles, int index, ParticleSystemState renderState)
        {
            var value = 0f;
            Fill(particles.Slice(index, 1), renderState, new Span<float>(ref value));
            return value;
        }
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

        public float NextNumber(ParticleSystemState renderState) => value;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values) => values.Fill(value);
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
        private float varyingValue;

        /// <param name="parse">The input block.</param>
        /// <param name="isBiased">Whether the draw goes through a bias curve.</param>
        /// <param name="defaultMode">The random mode when the block omits it.</param>
        public RandomNumberProvider(ParticleDefinitionParser parse, bool isBiased = false,
            ParticleFloatRandomMode defaultMode = ParticleFloatRandomMode.PF_RANDOM_MODE_CONSTANT)
        {
            parse.AddRandomInput();

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

        /// <summary>
        /// A varying input draws once per function call. A constant input reads the slot of particle
        /// id 0, as the collection has no particle of its own.
        /// </summary>
        public float NextNumber(ParticleSystemState renderState)
        {
            var random = renderState.Random;

            if (randomMode == ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING)
            {
                return VaryingOnce(random);
            }

            return Constant(random, inputSeed.Get(random), 0);
        }

        public bool IsPerParticle => true;

        /// <summary>
        /// A varying input draws once per block of four particles. A constant input reads a slot per
        /// particle id, so it fills from the id column; its seed is drawn by the first evaluation.
        /// </summary>
        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            if (particles.IsEmpty)
            {
                return;
            }

            var random = renderState.Random;

            if (randomMode == ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING)
            {
                foreach (var (offset, block) in particles.Blocks())
                {
                    values.Slice(offset, block.Length).Fill(Varying(random));
                }

                var fillEnd = particles.Start + particles.Length;
                var batchEnd = renderState.InitializerBatchEnd;
                var endsBlockRun = fillEnd == renderState.InitializerRunEnd && fillEnd == (batchEnd & ~3);

                if (endsBlockRun && batchEnd > fillEnd)
                {
                    Varying(random);
                }

                return;
            }

            var seed = inputSeed.Get(random);
            var particleIds = particles.ReadIntegers(ParticleField.ParticleId);

            for (var i = 0; i < particleIds.Length; i++)
            {
                values[i] = Constant(random, seed, particleIds[i]);
            }
        }

        /// <summary>Returns the varying value for the collection, drawing it only at the first read in the function call.</summary>
        private float VaryingOnce(ParticleRandom random)
        {
            var key = random.Evaluation();

            if (varyingKey != key)
            {
                varyingKey = key;
                varyingValue = Varying(random);
            }

            return varyingValue;
        }

        /// <summary>Draws a varying value, then its sign when the sign flips at random.</summary>
        private float Varying(ParticleRandom random)
        {
            var draw = random.Next();
            var sign = hasRandomSignFlip ? random.Next() : 1f;
            return Shape(draw, sign);
        }

        /// <summary>Reads the constant value of the particle with <paramref name="particleId"/>.</summary>
        private float Constant(ParticleRandom random, int seed, int particleId)
        {
            var draw = random.ForParticle(particleId, seed);
            var sign = hasRandomSignFlip ? random.ForParticle(particleId, seed + SignFlipOffset) : 1f;
            return Shape(draw, sign);
        }

        /// <summary>Turns a draw into the value in range, negated when the sign draw is below one half.</summary>
        private float Shape(float draw, float sign)
        {
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

        public float NextNumber(ParticleSystemState renderState)
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
        public float NextNumber(ParticleSystemState renderState) => attributeMapping.ApplyMapping(renderState.Age);

        /// <summary>Evaluates the input as if the system were <paramref name="age"/> seconds old.</summary>
        public float AtAge(float age) => attributeMapping.ApplyMapping(age);
    }

    // How long the endcap has been running, 0 outside it
    class EndCapAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public EndCapAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ParticleSystemState renderState) => attributeMapping.ApplyMapping(renderState.EndCapAge);
    }

    class DetailLevelNumberProvider : INumberProvider
    {
        private readonly float[] tiers;

        public DetailLevelNumberProvider(ParticleDefinitionParser parse)
        {
            tiers = [parse.Float("m_flLOD0"), parse.Float("m_flLOD1"), parse.Float("m_flLOD2"), parse.Float("m_flLOD3")];
        }

        public float NextNumber(ParticleSystemState renderState)
        {
            return tiers[Math.Clamp((int)renderState.DetailLevel, 0, tiers.Length - 1)];
        }
    }

    // Particle Age
    class ParticleAgeNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            TensorPrimitives.Subtract(particles.Time, particles.Floats(ParticleField.CreationTime), values);
            attributeMapping.ApplyMapping(values);
        }
    }

    // Particle Age (0-1)
    class ParticleAgeNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public ParticleAgeNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var lifetimes = particles.ReadFloats(ParticleField.LifeDuration);
            TensorPrimitives.Subtract(particles.Time, particles.Floats(ParticleField.CreationTime), values);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = lifetimes[i] <= 0f ? 0f : values[i] / lifetimes[i];
            }

            attributeMapping.ApplyMapping(values);
        }
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

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            Read(particles, field, values);
            mapping.ApplyMapping(values);
        }

        /// <summary>
        /// Reads <paramref name="field"/> of every particle in <paramref name="particles"/>: an integer
        /// attribute converted to its value, an angle in degrees, and 0 for a field that is not a scalar
        /// attribute.
        /// </summary>
        public static void Read(ParticleSpan particles, ParticleField field, Span<float> values)
        {
            if (!particles.TryGetFloats(field, out var scalars, out var constant))
            {
                var value = field.IsIntegerAttribute() ? BitConverter.SingleToInt32Bits(constant) : constant;
                values.Fill(field.IsAngleField() ? float.RadiansToDegrees(value) : value);
                return;
            }

            if (field.IsIntegerAttribute())
            {
                TensorPrimitives.ConvertTruncating<int, float>(MemoryMarshal.Cast<float, int>(scalars), values);
                return;
            }

            if (field.IsAngleField())
            {
                TensorPrimitives.RadiansToDegrees(scalars, values);
                return;
            }

            scalars.CopyTo(values);
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
            parse.AddInitialRead(field);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            PerParticleNumberProvider.Read(renderState.InitialOf(particles), field, values);
            mapping.ApplyMapping(values);
        }
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

            if (initial)
            {
                parse.AddInitialRead(field);
            }
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var source = initial ? renderState.InitialOf(particles) : particles;
            var vectors = source.ReadVectors(field);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = vectors[i].GetComponent(component);
            }

            mapping.ApplyMapping(values);
        }
    }

    // Particle Speed
    class PerParticleSpeedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleSpeedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }
        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var frameTime = renderState.Data?.CurrentFrameTime ?? 0f;
            var positions = particles.ReadVectors(ParticleField.Position);
            var previousPositions = particles.ReadVectors(ParticleField.PositionPrevious);

            for (var i = 0; i < values.Length; i++)
            {
                var velocity = ParticleVelocityVectorProvider.Velocity(positions[i], previousPositions[i], frameTime);
                values[i] = velocity.Length();
            }

            attributeMapping.ApplyMapping(values);
        }
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_NUMBER. The particle's place in the collection, counted back from the particle
    /// count when the order is reversed. Initializers instead read its particle number, and with the
    /// order reversed count back from the particle count by its place in the batch.
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

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Number(particles.Start + i, renderState);
            }

            attributeMapping.ApplyMapping(values);
        }

        private float Number(int index, ParticleSystemState renderState)
        {
            var place = index;

            if (renderState.InitializingParticles)
            {
                place -= renderState.InitializedFirst;
            }

            if (reverseOrder)
            {
                return renderState.ParticleCount - place;
            }

            if (renderState.InitializingParticles)
            {
                return renderState.ParticleNumber(index);
            }

            return place;
        }
    }

    // Particle Count Percent of Total Count (0-1)
    class PerParticleCountNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;
        public PerParticleCountNormalizedNumberProvider(ParticleDefinitionParser parse) { attributeMapping = new AttributeMapping(parse); }

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var divisor = renderState.Data?.BehaviorVersion >= 12
                ? Math.Max(renderState.ParticleCount - 1, 1)
                : Math.Max(renderState.ParticleCount, 1);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (particles.Start + i) * (1f / divisor);
            }

            attributeMapping.ApplyMapping(values);
        }
    }

    /// <summary>
    /// PF_TYPE_PARTICLE_ROPE_SEGMENT. The segment position held in X of <see cref="ParticleField.RopeSegmentData"/>,
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

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var segments = particles.ReadVectors(ParticleField.RopeSegmentData);

            for (var i = 0; i < values.Length; i++)
            {
                var data = segments[i];
                values[i] = reverseOrder ? data.Z - data.X : data.X;
            }

            attributeMapping.ApplyMapping(values);
        }
    }

    /// <summary>PF_TYPE_PARTICLE_ROPE_SEGMENT_NORMALIZED. Y of <see cref="ParticleField.RopeSegmentData"/>.</summary>
    class RopeSegmentNormalizedNumberProvider : INumberProvider
    {
        private readonly AttributeMapping attributeMapping;

        public RopeSegmentNormalizedNumberProvider(ParticleDefinitionParser parse)
        {
            attributeMapping = new AttributeMapping(parse);
        }

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var segments = particles.ReadVectors(ParticleField.RopeSegmentData);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = segments[i].Y;
            }

            attributeMapping.ApplyMapping(values);
        }
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState)
            => mapping.ApplyMapping(noise.Evaluate(
                controlPoint < 0 ? Vector3.Zero : renderState.GetControlPoint(controlPoint).Position,
                renderState.Age));

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var inputs = particles.ReadVectors(inputField);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = noise.Evaluate(inputs[i], renderState.Age);
            }

            mapping.ApplyMapping(values);
        }
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState)
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

        public float NextNumber(ParticleSystemState renderState) => 0f;

        public bool IsPerParticle => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<float> values)
        {
            var target = renderState.GetControlPoint(cp).Position;
            var positions = particles.ReadVectors(ParticleField.Position);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Vector3.Distance(positions[i], target);
            }

            attributeMapping.ApplyMapping(values);
        }
    }
}
