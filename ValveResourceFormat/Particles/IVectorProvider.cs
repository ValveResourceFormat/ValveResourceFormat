using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Provides a <see cref="Vector3"/> value that may vary per particle or per frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/CParticleVecInput">CParticleVecInput</seealso>
    internal interface IVectorProvider
    {
        /// <summary>
        /// Returns the next vector value, evaluated per particle.
        /// </summary>
        Vector3 NextVector(ref Particle particle, ParticleSystemState renderState);

        /// <summary>
        /// Returns the next vector value using system-level state only.
        /// </summary>
        Vector3 NextVector(ParticleSystemState renderState)
            => NextVector(ref Particle.Default, renderState);
    }

    /// <summary>The single render camera is always the closest one.</summary>
    class ClosestCameraPositionVectorProvider : IVectorProvider
    {
        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => renderState.CameraPosition;
    }

    readonly struct LiteralVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralVectorProvider(Vector3 value)
        {
            this.value = value;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => value;
    }

    /// <summary>
    /// An input evaluated for the whole collection, as a <c>CParticleCollectionVecInput</c> is. A read
    /// for a particle gets the collection value, which holds for the whole function call.
    /// </summary>
    readonly struct CollectionVectorInput : IVectorProvider
    {
        private readonly IVectorProvider input;

        public CollectionVectorInput(IVectorProvider input)
        {
            this.input = input;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => input.NextVector(renderState);

        public Vector3 NextVector(ParticleSystemState renderState) => input.NextVector(renderState);
    }

    // Literal Color
    readonly struct LiteralColorVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralColorVectorProvider(Vector3 value)
        {
            // Literal colors are authored as 0-255; providers return normalized 0-1 colors
            this.value = value / 255.0f;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => value;
    }

    /// <summary>
    /// PVEC_TYPE_PARTICLE_VECTOR and PVEC_TYPE_PARTICLE_INITIAL_VECTOR. A vector attribute scaled per
    /// component, the initial variant as the particle's initializers left it. Zero for the collection.
    /// </summary>
    readonly struct PerParticleVectorProvider : IVectorProvider
    {
        private readonly ParticleField vectorAttribute = ParticleField.Color;
        private readonly Vector3 vectorAttributeScale = Vector3.One;
        private readonly bool initial;

        public PerParticleVectorProvider(ParticleDefinitionParser parse, bool initial)
        {
            vectorAttribute = parse.ParticleField("m_nVectorAttribute", vectorAttribute);
            vectorAttributeScale = parse.Vector3("m_vVectorAttributeScale", vectorAttributeScale);
            this.initial = initial;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
        {
            ref var source = ref initial ? ref renderState.InitialOf(ref particle) : ref particle;
            return vectorAttributeScale * source.GetVector(vectorAttribute);
        }

        public Vector3 NextVector(ParticleSystemState renderState) => Vector3.Zero;
    }

    /// <summary>PVEC_TYPE_PARTICLE_VELOCITY. The particle's velocity scaled per component. Zero for the collection.</summary>
    readonly struct ParticleVelocityVectorProvider : IVectorProvider
    {
        private readonly Vector3 vectorAttributeScale = Vector3.One;

        /// <summary>The velocity at its own scale, which an input defaulting to this type reads.</summary>
        public ParticleVelocityVectorProvider()
        {
        }

        public ParticleVelocityVectorProvider(ParticleDefinitionParser parse)
        {
            vectorAttributeScale = parse.Vector3("m_vVectorAttributeScale", vectorAttributeScale);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => vectorAttributeScale * particle.GetVelocity(renderState.Data?.CurrentFrameTime ?? 0f);

        public Vector3 NextVector(ParticleSystemState renderState) => Vector3.Zero;
    }

    /// <summary>
    /// PVEC_TYPE_NAMED_VALUE. The system's vector named value, as it stands now when following it and
    /// as the system started with otherwise. Zero when it has none.
    /// </summary>
    readonly struct NamedValueVectorProvider : IVectorProvider
    {
        private readonly string name;
        private readonly bool follow;

        public NamedValueVectorProvider(ParticleDefinitionParser parse)
        {
            name = parse.String("m_NamedValue");
            follow = parse.Boolean("m_bFollowNamedValue");
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => renderState.Data?.NamedValues.GetVector(name, follow) ?? Vector3.Zero;
    }

    // CP Value
    readonly struct CPValueVectorProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 scale = Vector3.One;
        public CPValueVectorProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            scale = parse.Vector3("m_vCPValueScale", scale);
        }

        public CPValueVectorProvider(int controlPoint)
        {
            cp = controlPoint;
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
        {
            return renderState.GetControlPoint(cp).Position * scale;
        }
    }

    readonly struct CPDeltaVectorProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly int deltaCP;
        public CPDeltaVectorProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            deltaCP = parse.Int32("m_nDeltaControlPoint");
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
        {
            return renderState.GetControlPoint(cp).Position - renderState.GetControlPoint(deltaCP).Position;
        }
    }

    /// <summary>PVEC_TYPE_CP_RELATIVE_POSITION. An offset placed in the control point's frame.</summary>
    readonly struct CPRelativePositionProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 relativePosition = Vector3.Zero;
        public CPRelativePositionProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            relativePosition = parse.Vector3("m_vCPRelativePosition", relativePosition);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => ControlPointTransformProvider.TransformPosition(renderState, cp, relativePosition);
    }

    /// <summary>PVEC_TYPE_CP_RELATIVE_DIR. A direction rotated into the control point's frame.</summary>
    readonly struct CPRelativeDirectionProvider : IVectorProvider
    {
        private readonly int cp;
        private readonly Vector3 relativeDirection = Vector3.UnitX;
        public CPRelativeDirectionProvider(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint");
            relativeDirection = parse.Vector3("m_vCPRelativeDir", relativeDirection);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => ControlPointTransformProvider.TransformDirection(renderState, cp, relativeDirection);
    }

    /// <summary>
    /// PVEC_TYPE_RANDOM_UNIFORM. Each block of four particles in a function call takes twelve draws,
    /// four per axis, and a particle reads the draw of its lane in each axis. A collection read takes
    /// one such block per call and reads its first lane.
    /// </summary>
    class RandomUniformVectorProvider : IVectorProvider
    {
        private const float MinimumRangeSquared = 1.1920929e-7f;
        private const int BlockDraws = 12;

        private readonly Vector3 min;
        private readonly Vector3 max;

        /// <summary>
        /// Whether the range is a single point. Such a range returns its endpoint and takes no draw at
        /// all, so it leaves the position of every later draw in the system unchanged.
        /// </summary>
        private readonly bool isDegenerate;

        private ParticleRandom.EvaluationKey? blockKey;
        private int blockCounter;

        public RandomUniformVectorProvider(ParticleDefinitionParser parse)
        {
            min = parse.Vector3("m_vRandomMin");
            max = parse.Vector3("m_vRandomMax");
            isDegenerate = Vector3.DistanceSquared(min, max) <= MinimumRangeSquared;
        }

        public virtual Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => Draw(renderState.Random, particle.Index);

        public virtual Vector3 NextVector(ParticleSystemState renderState) => Draw(renderState.Random, -1);

        private Vector3 Draw(ParticleRandom random, int particleIndex)
        {
            if (isDegenerate)
            {
                return min;
            }

            var key = random.Evaluation(particleIndex);

            if (blockKey != key)
            {
                blockKey = key;
                blockCounter = random.Reserve(BlockDraws);
            }

            var lane = blockCounter + (particleIndex < 0 ? 0 : particleIndex & 3);
            var range = max - min;

            return new Vector3(
                (range.X * random.Drawn(lane)) + min.X,
                (range.Y * random.Drawn(lane + 4)) + min.Y,
                (range.Z * random.Drawn(lane + 8)) + min.Z);
        }
    }

    class RandomUniformOffsetVectorProvider : RandomUniformVectorProvider
    {
        private readonly ParticleField vectorAttribute = ParticleField.Color;
        private readonly Vector3 vectorAttributeScale = Vector3.One;

        public RandomUniformOffsetVectorProvider(ParticleDefinitionParser parse) : base(parse)
        {
            vectorAttribute = parse.ParticleField("m_nVectorAttribute", vectorAttribute);
            vectorAttributeScale = parse.Vector3("m_vVectorAttributeScale", vectorAttributeScale);
        }

        public override Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => (vectorAttributeScale * particle.GetVector(vectorAttribute)) + base.NextVector(ref particle, renderState);

        public override Vector3 NextVector(ParticleSystemState renderState)
            => (vectorAttributeScale * Particle.Default.GetVector(vectorAttribute)) + base.NextVector(renderState);
    }

    /// <summary>
    /// PVEC_TYPE_CP_RELATIVE_RANDOM_DIR. A random vector rotated into the control point's frame as it
    /// stood when the particle was created.
    /// </summary>
    class CPRelativeRandomDirectionProvider : RandomUniformVectorProvider
    {
        private readonly ControlPointTransformProvider controlPoint;

        public CPRelativeRandomDirectionProvider(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = new ControlPointTransformProvider(parse.Int32("m_nControlPoint"), true);
        }

        public override Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => Vector3.TransformNormal(base.NextVector(ref particle, renderState),
                controlPoint.NextTransformAtTime(ref particle, renderState, particle.CreationTime));

        public override Vector3 NextVector(ParticleSystemState renderState)
            => Vector3.TransformNormal(base.NextVector(renderState), controlPoint.NextTransform(ref Particle.Default, renderState));
    }

    // 3 Float Inputs
    readonly struct FloatComponentsVectorProvider : IVectorProvider
    {
        private readonly INumberProvider x = new LiteralNumberProvider(0);
        private readonly INumberProvider y = new LiteralNumberProvider(0);
        private readonly INumberProvider z = new LiteralNumberProvider(0);

        public FloatComponentsVectorProvider(ParticleDefinitionParser parse)
        {
            x = parse.NumberProvider("m_FloatComponentX", x);
            y = parse.NumberProvider("m_FloatComponentY", y);
            z = parse.NumberProvider("m_FloatComponentZ", z);
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState) => new(
            x.NextNumber(ref particle, renderState),
            y.NextNumber(ref particle, renderState),
            z.NextNumber(ref particle, renderState));

        public Vector3 NextVector(ParticleSystemState renderState) => new(
            x.NextNumber(renderState),
            y.NextNumber(renderState),
            z.NextNumber(renderState));
    }

    /// <summary>
    /// PVEC_TYPE_FLOAT_INTERP_CLAMPED, PVEC_TYPE_FLOAT_INTERP_OPEN and PVEC_TYPE_FLOAT_INTERP_GRADIENT.
    /// A float input placed in its input range, then blended between the two outputs or looked up in
    /// the gradient.
    /// </summary>
    readonly struct FloatInterpolationVectorProvider : IVectorProvider
    {
        private readonly INumberProvider floatInterp = new LiteralNumberProvider(0);
        private readonly float input0;
        private readonly float input1 = 1f;
        private readonly Vector3 output0;
        private readonly Vector3 output1 = Vector3.One;
        private readonly bool clamp;
        private readonly ColorGradient? gradient;

        public FloatInterpolationVectorProvider(ParticleDefinitionParser parse, bool isClamped, bool isGradient)
        {
            floatInterp = parse.NumberProvider("m_FloatInterp", floatInterp);
            input0 = parse.Float("m_flInterpInput0", input0);
            input1 = parse.Float("m_flInterpInput1", input1);
            output0 = parse.Vector3("m_vInterpOutput0", output0);
            output1 = parse.Vector3("m_vInterpOutput1", output1);
            clamp = isClamped;

            if (isGradient)
            {
                gradient = new ColorGradient(parse.Data.GetSubCollection("m_Gradient"));
            }
        }

        public Vector3 NextVector(ref Particle particle, ParticleSystemState renderState)
            => Interpolate(floatInterp.NextNumber(ref particle, renderState));

        public Vector3 NextVector(ParticleSystemState renderState)
            => Interpolate(floatInterp.NextNumber(renderState));

        private Vector3 Interpolate(float value)
        {
            var t = (value - input0) / (input1 - input0);

            if (gradient != null)
            {
                return gradient.Evaluate(t);
            }

            if (clamp)
            {
                t = MathF.Max(0f, MathF.Min(1f, t));
            }

            return ((output1 - output0) * t) + output0;
        }
    }

    /* NOISE PARAMS
     * 		m_flNoiseOutputMin = 0.000000
			m_flNoiseOutputMax = 1.000000
			m_flNoiseScale = 0.100000
			m_vecNoiseOffsetRate =
			[
				0.000000,
				0.000000,
				0.000000,
			]
			m_flNoiseOffset = 0.000000
			m_nNoiseOctaves = 1
			m_nNoiseTurbulence = "PF_NOISE_TURB_NONE"
			m_nNoiseType = "PF_NOISE_TYPE_PERLIN"
			m_nNoiseModifier = "PF_NOISE_MODIFIER_NONE"
			m_flNoiseTurbulenceScale = 1.000000
			m_flNoiseTurbulenceMix = 0.500000
			m_flNoiseImgPreviewScale = 1.000000
			m_bNoiseImgPreviewLive = true
    */
}
