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

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.Radius);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var rampStart = warpMin;
            var rampEnd = warpMax;

            if (scaleControlPointNumber >= 0)
            {
                var scale = particleSystemState.GetControlPoint(scaleControlPointNumber).Position;
                rampStart *= scale;
                rampEnd *= scale;
            }

            var warp = MathUtils.Lerp(rampStart, rampEnd, inputValue.NextNumber(ref particle, particleSystemState));

            var frame = particleSystemState.GetControlPoint(controlPointNumber).GetBasisAtTime(particle.CreationTime);
            var origin = frame.Translation;
            var toLocal = Matrix4x4.Transpose(frame with { Translation = Vector3.Zero });

            var local = Vector3.TransformNormal(particle.Position - origin, toLocal);
            particle.Position = Vector3.Transform(local * warp, frame);

            var localPrevious = Vector3.TransformNormal(particle.PositionPrevious - origin, toLocal);
            particle.PositionPrevious = Vector3.Transform(prevPosScale * warp * localPrevious, frame);

            return particle;
        }
    }
}
