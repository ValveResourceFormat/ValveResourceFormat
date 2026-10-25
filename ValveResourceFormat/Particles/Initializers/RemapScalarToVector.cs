using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Remaps a scalar particle field into a vector field by interpolating between a minimum and
    /// maximum output vector. A position output is placed around a control point, in its frame with
    /// local coordinates, which is how muzzle flashes lay their sprites out along the barrel axis.
    /// Other outputs are written as remapped. Creation times are measured from the last restart.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapScalarToVector">C_INIT_RemapScalarToVector</seealso>
    class RemapScalarToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.CreationTime;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly int controlPoint;
        private readonly bool localCoords = true;
        private readonly float remapBias = 0.5f;

        public RemapScalarToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            remapBias = parse.Float("m_flRemapBias", remapBias);
        }

        public override ulong WrittenFields => fieldOutput.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var timeBase = particleSystemState.OperatorTimeBase;
            var deltaTime = particleSystemState.CurrentStep;
            var hasWindow = startTime != -1f && endTime != -1f;
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var inputs = run.ReadFloats(fieldInput);
            var outputs = run.Vectors(fieldOutput);
            var previousPositions = fieldOutput == ParticleField.Position ? run.Vectors(ParticleField.PositionPrevious) : default;

            for (var i = 0; i < run.Length; i++)
            {
                var creationTime = creationTimes[i];
                var age = MathF.Max(0f, creationTime - timeBase);

                // A -1 on either time bound means the window is unset and the remap always runs.
                if (hasWindow && (age < startTime || age >= endTime))
                {
                    continue;
                }

                var input = inputs[i];

                if (fieldInput == ParticleField.CreationTime)
                {
                    input = MathF.Max(0f, input - timeBase);
                }

                var value = new Vector3(
                    RemapComponent(input, outputMin.X, outputMax.X),
                    RemapComponent(input, outputMin.Y, outputMax.Y),
                    RemapComponent(input, outputMin.Z, outputMax.Z));

                if (fieldOutput == ParticleField.Position)
                {
                    WritePosition(ref outputs[i], ref previousPositions[i], creationTime, deltaTime, particleSystemState, value);
                    continue;
                }

                var current = outputs[i];

                outputs[i] = setMethod == ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE
                    ? (deltaTime * current) + value
                    : ParticleFieldExtensions.ApplySetMethod(value, current, current, deltaTime, setMethod);
            }
        }

        private float RemapComponent(float input, float outMin, float outMax)
        {
            var value = MathUtils.RemapValClamped(input, inputMin, inputMax, outMin, outMax);

            return remapBias != 0.5f ? ParticleMath.Bias(MathUtils.Saturate(value), remapBias) : value;
        }

        /// <summary>
        /// Places the particle. In local coordinates the value is a point in the control point's frame and
        /// the particle starts at rest; otherwise it is an offset from the control point, and the set
        /// method combines it with the position and the previous position separately.
        /// </summary>
        private void WritePosition(ref Vector3 position, ref Vector3 previousPosition, float creationTime, float deltaTime, ParticleSystemState particleSystemState, Vector3 value)
        {
            if (localCoords)
            {
                var frame = particleSystemState.GetControlPoint(controlPoint).GetBasisAtTime(creationTime);
                var local = Vector3.Transform(value, frame);
                local = ParticleFieldExtensions.ApplySetMethod(local, position, position, deltaTime, setMethod);

                position = local;
                previousPosition = local;
                return;
            }

            var offset = ParticlePath.GetControlPointAtTime(particleSystemState, controlPoint, creationTime) + value;

            position = ParticleFieldExtensions.ApplySetMethod(offset, position, position, deltaTime, setMethod);
            previousPosition = ParticleFieldExtensions.ApplySetMethod(offset, previousPosition, previousPosition, deltaTime, setMethod);
        }
    }
}
