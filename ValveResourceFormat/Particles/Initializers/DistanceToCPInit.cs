using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar attribute at spawn from the particle's distance to a control point, remapped
    /// between input and output ranges with optional Schlick bias, per-component distance scaling,
    /// and an active range that skips particles outside it. With line of sight, a trace from the control
    /// point toward the particle that hits the world scales the distance by the hit fraction and
    /// <c>m_flLOSScale</c>; the trace ignores the collision group and trace set.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_DistanceToCPInit">C_INIT_DistanceToCPInit</seealso>
    class DistanceToCPInit : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly int startCP;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly Vector3 distanceScale = Vector3.One;
        private readonly float remapBias = 0.5f;
        private readonly bool hasBias;
        private readonly bool lineOfSight;
        private readonly INumberProvider maxTraceLength = new LiteralNumberProvider(-1f);
        private readonly float lineOfSightScale;

        public DistanceToCPInit(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            startCP = parse.ControlPoint("m_nStartCP", startCP);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            distanceScale = parse.Vector3("m_vecDistanceScale", distanceScale);
            remapBias = parse.Float("m_flRemapBias", remapBias);
            hasBias = remapBias != 0.5f;
            lineOfSight = parse.Boolean("m_bLOS", lineOfSight);
            maxTraceLength = parse.NumberProvider("m_flMaxTraceLength", maxTraceLength);
            lineOfSightScale = parse.Float("m_flLOSScale", lineOfSightScale);
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var controlPointPosition = particleSystemState.GetControlPoint(startCP).Position;

            var inMins = inputMin.Fill(run, particleSystemState);
            var inMaxs = inputMax.Fill(run, particleSystemState);
            var outMins = outputMin.Fill(run, particleSystemState);
            var outMaxs = outputMax.Fill(run, particleSystemState);
            var traceLengths = maxTraceLength.Fill(run, particleSystemState);

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var positions = run.ReadVectors(ParticleField.Position);
            var outputs = run.Floats(fieldOutput);
            var clampOutput = fieldOutput.IsAlpha();

            for (var i = 0; i < run.Length; i++)
            {
                var inMin = inMins[i];
                var inMax = inMaxs[i];
                var outMin = outMins[i];
                var outMax = outMaxs[i];

                if (clampOutput)
                {
                    outMin = MathUtils.Saturate(outMin);
                    outMax = MathUtils.Saturate(outMax);
                }

                var distance = ((controlPointPosition - positions[i]) * distanceScale).Length();

                if (activeRange && (distance < inMin || distance > inMax))
                {
                    continue;
                }

                if (lineOfSight)
                {
                    distance = LineOfSightTrace.ScaleDistance(particleSystemState.Collision, controlPointPosition, positions[i], distance, traceLengths[i], lineOfSightScale);
                }

                var value = MathUtils.RemapValClamped(distance, inMin, inMax, outMin, outMax);

                if (hasBias)
                {
                    value = ParticleMath.BiasClamped(value, remapBias);
                }

                var current = outputs[i];
                outputs[i] = ParticleFieldExtensions.ApplySetMethod(value, current, current, particleSystemState.CurrentStep, setMethod);
            }
        }
    }
}
