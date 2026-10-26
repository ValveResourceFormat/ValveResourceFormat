using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the distance between two transform origins into a scalar output field. Particle
    /// positions are never read; only the per-particle range inputs vary the result across
    /// particles.
    /// </summary>
    /// <remarks>
    /// With <c>m_bLOS</c> a trace runs from the start transform toward the end one, and a blocked
    /// trace scales the distance by the hit fraction times <c>m_flLOSScale</c>, so with the default
    /// scale of 0 any hit makes the distance 0. A distance longer than <c>m_flMaxTraceLength</c>
    /// shortens the trace to that length along the direction of the end position from the world
    /// origin, not from the start transform. <c>m_CollisionGroupName</c> and <c>m_nTraceSet</c> are
    /// not read; the trace hits all world collision.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceBetweenTransforms">C_OP_DistanceBetweenTransforms</seealso>
    class DistanceBetweenTransforms : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly bool los;
        private readonly float maxTraceLength = -1f;
        private readonly float losScale;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public DistanceBetweenTransforms(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            los = parse.Boolean("m_bLOS", los);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            losScale = parse.Float("m_flLOSScale", losScale);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var distance = Vector3.Distance(start, end);

            if (los)
            {
                distance = LineOfSightTrace.ScaleDistance(particleSystemState.Collision, start, end, distance, maxTraceLength, losScale);
            }

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var inputMins = inputMin.Fill(current, particleSystemState);
            var inputMaxs = inputMax.Fill(current, particleSystemState);
            var outputMins = outputMin.Fill(current, particleSystemState);
            var outputMaxs = outputMax.Fill(current, particleSystemState);

            var outputs = current.Floats(outputField);
            var initials = particles.Initial.ReadFloats(outputField);

            for (var i = 0; i < outputs.Length; i++)
            {
                var remapped = ParticleMath.ClampedFraction(distance, inputMins[i], inputMaxs[i]);
                var finalValue = MathUtils.Lerp(outputMins[i], outputMaxs[i], remapped);

                finalValue = ParticleFieldExtensions.ApplySetMethod(finalValue, initials[i], outputs[i], particleSystemState.CurrentStep, setMethod);

                outputs[i] = MathUtils.Lerp(outputs[i], finalValue, strength);
            }
        }
    }
}
