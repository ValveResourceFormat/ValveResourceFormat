namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Scales each particle's spawn position and previous position about a control point's frame by
    /// a warp vector, placed between a minimum and a maximum by a per-particle input. The input is not
    /// clamped, so values outside 0 to 1 extrapolate past the two ends.
    /// </summary>
    /// <remarks>
    /// The radius is declared as written for the first-writer rule but never changed.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionWarpScalar">C_INIT_PositionWarpScalar</seealso>
    class PositionWarpScalar : ParticleFunctionInitializer
    {
        private readonly Vector3 warpMin = Vector3.One;
        private readonly Vector3 warpMax = Vector3.One;
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0f);
        private readonly float prevPosScale = 1f;
        private readonly int scaleControlPointNumber = -1;
        private readonly int controlPointNumber;

        public PositionWarpScalar(ParticleDefinitionParser parse) : base(parse)
        {
            warpMin = parse.Vector3("m_vecWarpMin", warpMin);
            warpMax = parse.Vector3("m_vecWarpMax", warpMax);
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            scaleControlPointNumber = parse.Int32("m_nScaleControlPointNumber", scaleControlPointNumber);
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(scaleControlPointNumber);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Radius.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var inputValues = inputValue.Fill(run, particleSystemState);

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);

            var rampStart = warpMin;
            var rampEnd = warpMax;

            if (scaleControlPointNumber >= 0)
            {
                var scale = particleSystemState.GetControlPoint(scaleControlPointNumber).Position;
                rampStart *= scale;
                rampEnd *= scale;
            }

            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            for (var i = 0; i < run.Length; i++)
            {
                var warp = Vector3.Lerp(rampStart, rampEnd, inputValues[i]);

                var frame = controlPoint.GetBasisAtTime(creationTimes[i]);
                PositionWarp.Warp(frame, warp, prevPosScale, ref positions[i], ref previousPositions[i]);
            }
        }
    }
}
