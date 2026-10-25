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
        /// Returns the value for the whole collection, as emitters and renderers read it. Inputs that
        /// read a particle return zero here.
        /// </summary>
        Vector3 NextVector(ParticleSystemState renderState);

        /// <summary>
        /// Writes the value for each particle of <paramref name="particles"/> into
        /// <paramref name="values"/>, which holds one entry per particle. An input that reads no particle
        /// writes its collection value, and evaluates nothing for an empty span.
        /// </summary>
        void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            if (!particles.IsEmpty)
            {
                values.Fill(NextVector(renderState));
            }
        }

        /// <summary>
        /// Evaluates the value for each particle of <paramref name="particles"/> as
        /// <see cref="Fill(ParticleSpan, ParticleSystemState, Span{Vector3})"/> does, into a scratch column
        /// valid until the running function returns.
        /// </summary>
        Span<Vector3> Fill(ParticleSpan particles, ParticleSystemState renderState)
        {
            var values = ParticleScratch.Vectors(particles.Length);
            Fill(particles, renderState, values);
            return values;
        }

        /// <summary>
        /// Whether the input takes its random draws block by block as the function walks its particles,
        /// rather than when the function sets its inputs up.
        /// </summary>
        bool DrawsPerBlock => false;

        /// <summary>
        /// Writes the values of <paramref name="block"/>, the up to four particles one evaluation of the
        /// input covers, the first of which reads lane <paramref name="lane"/> of it. Only an input that
        /// <see cref="DrawsPerBlock"/> fills a block differently from any other span.
        /// </summary>
        void FillBlock(ParticleSpan block, int lane, ParticleSystemState renderState, Span<Vector3> values)
            => Fill(block, renderState, values);

        /// <summary>
        /// Sets the input up for a function call over <paramref name="particles"/>, as the function does
        /// with each of its inputs before walking them. Returns a scratch column valid until the running
        /// function returns, filled now unless the input <see cref="DrawsPerBlock"/>, in which case
        /// <see cref="ParticleSpan.FillPerBlockInputs"/> fills it.
        /// </summary>
        Span<Vector3> Prepare(ParticleSpan particles, ParticleSystemState renderState)
        {
            var values = ParticleScratch.Vectors(particles.Length);

            if (!DrawsPerBlock)
            {
                Fill(particles, renderState, values);
            }

            return values;
        }

        /// <summary>
        /// Sets the input up as <see cref="Prepare(ParticleSpan, ParticleSystemState)"/> does, for a function
        /// that sets it up over <paramref name="evaluated"/>, which contains <paramref name="particles"/>,
        /// such as every live particle. Returns the column of <paramref name="particles"/>, each of them
        /// reading its own value.
        /// </summary>
        Span<Vector3> Prepare(ParticleSpan particles, ParticleSpan evaluated, ParticleSystemState renderState)
            => Prepare(evaluated, renderState).Slice(particles.Start - evaluated.Start, particles.Length);

        /// <summary>Returns the value <see cref="Fill(ParticleSpan, ParticleSystemState, Span{Vector3})"/> writes for the particle at <paramref name="index"/> of <paramref name="particles"/>.</summary>
        public Vector3 NextVector(ParticleSpan particles, int index, ParticleSystemState renderState)
        {
            var value = Vector3.Zero;
            Fill(particles.Slice(index, 1), renderState, new Span<Vector3>(ref value));
            return value;
        }
    }

    /// <summary>The single render camera is always the closest one.</summary>
    class ClosestCameraPositionVectorProvider : IVectorProvider
    {
        public Vector3 NextVector(ParticleSystemState renderState) => renderState.CameraPosition;
    }

    readonly struct LiteralVectorProvider : IVectorProvider
    {
        private readonly Vector3 value;

        public LiteralVectorProvider(Vector3 value)
        {
            this.value = value;
        }

        public Vector3 NextVector(ParticleSystemState renderState) => value;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values) => values.Fill(value);
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

        public Vector3 NextVector(ParticleSystemState renderState) => value;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values) => values.Fill(value);
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

            if (initial)
            {
                parse.AddInitialRead(vectorAttribute);
            }
        }

        public Vector3 NextVector(ParticleSystemState renderState) => Vector3.Zero;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var source = initial ? renderState.InitialOf(particles) : particles;
            var vectors = source.ReadVectors(vectorAttribute);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = vectorAttributeScale * vectors[i];
            }
        }
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

        public Vector3 NextVector(ParticleSystemState renderState) => Vector3.Zero;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var frameTime = renderState.Data?.CurrentFrameTime ?? 0f;
            var positions = particles.ReadVectors(ParticleField.Position);
            var previousPositions = particles.ReadVectors(ParticleField.PositionPrevious);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = vectorAttributeScale * Velocity(positions[i], previousPositions[i], frameTime);
            }
        }

        /// <summary>
        /// The velocity of a particle that moved from <paramref name="previous"/> to
        /// <paramref name="position"/> over <paramref name="frameTime"/>, or zero for no time.
        /// </summary>
        public static Vector3 Velocity(Vector3 position, Vector3 previous, float frameTime)
            => frameTime > 0f ? (position - previous) / frameTime : Vector3.Zero;
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

        public Vector3 NextVector(ParticleSystemState renderState)
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

        public Vector3 NextVector(ParticleSystemState renderState)
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

        public Vector3 NextVector(ParticleSystemState renderState)
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

        public Vector3 NextVector(ParticleSystemState renderState)
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

        public Vector3 NextVector(ParticleSystemState renderState)
            => ControlPointTransformProvider.TransformDirection(renderState, cp, relativeDirection);
    }

    /// <summary>
    /// PVEC_TYPE_RANDOM_UNIFORM. Each block of four particles in a function call takes twelve draws,
    /// four per axis, and a particle reads the draw of its lane in each axis. A collection read takes
    /// one such block per call and reads its first lane.
    /// </summary>
    class RandomUniformVectorProvider : IVectorProvider
    {
        private const float MinimumRangeSquared = ParticleMath.FloatEpsilon;
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

        public virtual Vector3 NextVector(ParticleSystemState renderState) => DrawOnce(renderState.Random);

        public bool DrawsPerBlock => true;

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            foreach (var (offset, block) in particles.Blocks())
            {
                FillBlock(block, block.Start & 3, renderState, values.Slice(offset, block.Length));
            }
        }

        public void FillBlock(ParticleSpan block, int lane, ParticleSystemState renderState, Span<Vector3> values)
        {
            if (isDegenerate)
            {
                values.Fill(min);
            }
            else
            {
                var random = renderState.Random;
                var counter = random.Reserve(BlockDraws);

                for (var i = 0; i < block.Length; i++)
                {
                    values[i] = Lane(random, counter, lane + i);
                }
            }

            Apply(block, renderState, values);
        }

        /// <summary>
        /// Turns the drawn <paramref name="values"/> of <paramref name="particles"/> into the input's
        /// values, for the input types built on this one.
        /// </summary>
        protected virtual void Apply(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
        }

        /// <summary>
        /// Returns the drawn value for the collection, drawing its block only at the first read in the
        /// function call.
        /// </summary>
        private Vector3 DrawOnce(ParticleRandom random)
        {
            if (isDegenerate)
            {
                return min;
            }

            var key = random.Evaluation();

            if (blockKey != key)
            {
                blockKey = key;
                blockCounter = random.Reserve(BlockDraws);
            }

            return Lane(random, blockCounter, 0);
        }

        /// <summary>Reads the value of <paramref name="lane"/> from the block whose draws start at <paramref name="counter"/>.</summary>
        private Vector3 Lane(ParticleRandom random, int counter, int lane)
        {
            var first = counter + lane;
            return MathUtils.Lerp(min, max, new Vector3(random.Drawn(first), random.Drawn(first + 4), random.Drawn(first + 8)));
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

        /// <summary>
        /// The collection reads the attribute of the first slot of its particles, live or not.
        /// </summary>
        public override Vector3 NextVector(ParticleSystemState renderState)
        {
            var value = base.NextVector(renderState);
            var attribute = renderState.Data?.Particles.FirstSlotVector(vectorAttribute) ?? Vector3.Zero;
            return (vectorAttributeScale * attribute) + value;
        }

        protected override void Apply(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var vectors = particles.ReadVectors(vectorAttribute);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (vectorAttributeScale * vectors[i]) + values[i];
            }
        }
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

        public override Vector3 NextVector(ParticleSystemState renderState)
            => Vector3.TransformNormal(base.NextVector(renderState), controlPoint.NextTransform(renderState));

        protected override void Apply(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var creationTimes = particles.ReadFloats(ParticleField.CreationTime);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Vector3.TransformNormal(values[i], controlPoint.NextTransformAtTime(renderState, creationTimes[i]));
            }
        }
    }

    /// <summary>
    /// PVEC_TYPE_FLOAT_COMPONENTS. Three float inputs, each evaluated over all the particles before the
    /// next, so all of X's random draws come before Y's and Z's.
    /// </summary>
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

        public Vector3 NextVector(ParticleSystemState renderState) => new(
            x.NextNumber(renderState),
            y.NextNumber(renderState),
            z.NextNumber(renderState));

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var evaluated = renderState.VectorInputParticles(particles);
            var xs = x.Fill(evaluated, renderState);
            var ys = y.Fill(evaluated, renderState);
            var zs = z.Fill(evaluated, renderState);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = new Vector3(xs[i], ys[i], zs[i]);
            }
        }
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

        public Vector3 NextVector(ParticleSystemState renderState)
            => Interpolate(floatInterp.NextNumber(renderState));

        public void Fill(ParticleSpan particles, ParticleSystemState renderState, Span<Vector3> values)
        {
            var inputs = floatInterp.Fill(renderState.VectorInputParticles(particles), renderState);

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Interpolate(inputs[i]);
            }
        }

        private Vector3 Interpolate(float value)
        {
            var t = (value - input0) / (input1 - input0);

            if (gradient != null)
            {
                return gradient.Evaluate(t);
            }

            if (clamp)
            {
                t = MathUtils.Saturate(t);
            }

            return MathUtils.Lerp(output0, output1, t);
        }
    }
}
