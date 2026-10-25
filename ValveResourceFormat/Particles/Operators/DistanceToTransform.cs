using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's component-scaled distance from a transform's origin into a scalar
    /// output field. The remap runs entirely in squared-distance space - the input range ends are
    /// squared and the distance is never square-rooted - so the response is quadratic in distance.
    /// </summary>
    /// <remarks>
    /// With <c>m_bLOS</c> a trace runs from the transform to each particle that passes the active
    /// range, and a blocked trace scales the squared distance by the hit fraction times
    /// <c>m_flLOSScale</c>, so with the default scale of 0 any hit makes it 0. When the squared
    /// distance exceeds <c>m_flMaxTraceLength</c> (compared unsquared), the trace is shortened to that
    /// length along the direction of the particle position from the world origin.
    /// <c>m_CollisionGroupName</c> and <c>m_nTraceSet</c> are not read; the trace hits all world
    /// collision.
    /// <para>
    /// Particles are processed in index-aligned blocks of four: with the active-range test on, a
    /// block where every lane fails is skipped without any writes, while a failed lane in a partially
    /// passing block is written with zero strength, which on the additive path still doubles the old
    /// value.
    /// </para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceToTransform">C_OP_DistanceToTransform</seealso>
    class DistanceToTransform : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly bool los;
        private readonly float maxTraceLength = -1f;
        private readonly float losScale;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool additive;
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);

        public DistanceToTransform(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            los = parse.Boolean("m_bLOS", los);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            losScale = parse.Float("m_flLOSScale", losScale);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            additive = parse.Boolean("m_bAdditive", additive);
            componentScale = parse.VectorProvider("m_vecComponentScale", componentScale);

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var origin = transformStart.NextTransform(particleSystemState).Translation;
            var current = particles.Current;
            var clampOutput = outputField.IsAlpha();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var scales = componentScale.Prepare(current, particleSystemState);
            var inputMins = inputMin.Fill(current, particleSystemState);
            var inputMaxs = inputMax.Fill(current, particleSystemState);
            var outputMins = outputMin.Fill(current, particleSystemState);
            var outputMaxs = outputMax.Fill(current, particleSystemState);

            current.FillPerBlockInputs(particleSystemState, componentScale, scales);

            var positions = current.ReadVectors(ParticleField.Position);
            var outputs = current.Floats(outputField);
            var initials = particles.Initial.ReadFloats(outputField);

            Span<float> values = stackalloc float[4];
            Span<bool> pass = stackalloc bool[4];

            for (var offset = 0; offset < current.Length; offset += 4)
            {
                var anyPass = false;
                var blockLength = Math.Min(4, current.Length - offset);

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var index = offset + lane;
                    var position = positions[index];

                    var minimumInput = inputMins[index];
                    var maximumInput = inputMaxs[index];
                    var minimumOutput = outputMins[index];
                    var maximumOutput = outputMaxs[index];

                    var scaledDelta = (position - origin) * scales[index];
                    var distanceSquared = Vector3.Dot(scaledDelta, scaledDelta);
                    var inputMinSquared = minimumInput * minimumInput;
                    var inputMaxSquared = maximumInput * maximumInput;

                    pass[lane] = !activeRange || (distanceSquared <= inputMaxSquared && inputMinSquared <= distanceSquared);
                    anyPass |= pass[lane];

                    if (los && pass[lane])
                    {
                        distanceSquared = LineOfSightTrace.ScaleDistance(particleSystemState.Collision, origin, position, distanceSquared, maxTraceLength, losScale);
                    }

                    if (clampOutput)
                    {
                        minimumOutput = MathUtils.Saturate(minimumOutput);
                        maximumOutput = MathUtils.Saturate(maximumOutput);
                    }

                    var remapped = ParticleMath.ClampedFraction(distanceSquared, inputMinSquared, inputMaxSquared);
                    values[lane] = MathUtils.Lerp(minimumOutput, maximumOutput, remapped);
                }

                if (!anyPass)
                {
                    continue;
                }

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var index = offset + lane;
                    var laneStrength = pass[lane] ? strength : 0f;

                    outputs[index] = BlendRemapped(values[lane], initials[index], outputs[index], particleSystemState.CurrentStep, setMethod, laneStrength, additive);
                }
            }
        }

        /// <summary>
        /// The new value of a lane in a block with a passing lane: the remapped <paramref name="value"/>
        /// applied by the set method and blended in at <paramref name="laneStrength"/>, which is zero for
        /// a lane that failed. The additive path adds the current value once more, so even a lane at no
        /// strength doubles.
        /// </summary>
        internal static float BlendRemapped(float value, float initial, float currentValue, float deltaTime, ParticleSetMethod setMethod,
            float laneStrength, bool additive)
        {
            var finalValue = ParticleFieldExtensions.ApplySetMethod(value, initial, currentValue, deltaTime, setMethod);
            var delta = (finalValue - currentValue) * laneStrength;

            return additive ? currentValue + (currentValue + delta) : currentValue + delta;
        }
    }
}
