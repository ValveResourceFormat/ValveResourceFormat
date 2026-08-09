namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Scales each particle's spawn position, its previous position and optionally its radius about a
    /// control point's frame by a warp vector between a minimum and a maximum. The vector is drawn per
    /// component at random, or, once a warp time is given, ramped from the minimum to the maximum across
    /// the warp window without any random draw.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionWarp">C_INIT_PositionWarp</seealso>
    class PositionWarp : ParticleFunctionInitializer
    {
        private readonly IVectorProvider warpMin = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider warpMax = new LiteralVectorProvider(Vector3.One);
        private readonly int controlPointNumber;
        private readonly int scaleControlPointNumber = -1;
        private readonly int radiusComponent = -1;
        private readonly float warpTime;
        private readonly float warpStartTime;
        private readonly float prevPosScale = 1f;
        private readonly bool invertWarp;
        private readonly bool useCount;

        /// <summary>How many particles the ramp has been driven over, when it runs on a count.</summary>
        private int warpCount;

        public PositionWarp(ParticleDefinitionParser parse) : base(parse)
        {
            warpMin = parse.VectorProvider("m_vecWarpMin", warpMin);
            warpMax = parse.VectorProvider("m_vecWarpMax", warpMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            scaleControlPointNumber = parse.Int32("m_nScaleControlPointNumber", scaleControlPointNumber);
            radiusComponent = Math.Clamp(parse.Int32("m_nRadiusComponent", radiusComponent), -1, 2);
            warpTime = parse.Float("m_flWarpTime", warpTime);
            warpStartTime = parse.Float("m_flWarpStartTime", warpStartTime);
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            invertWarp = parse.Boolean("m_bInvertWarp", invertWarp);
            useCount = parse.Boolean("m_bUseCount", useCount);
        }

        public override void Reset()
        {
            warpCount = 0;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var rampStart = warpMin.NextVector(ref particle, particleSystemState);
            var rampEnd = warpMax.NextVector(ref particle, particleSystemState);

            if (invertWarp)
            {
                (rampStart, rampEnd) = (rampEnd, rampStart);
            }

            if (scaleControlPointNumber >= 0)
            {
                var scale = particleSystemState.GetControlPoint(scaleControlPointNumber).Position;
                rampStart *= scale;
                rampEnd *= scale;
            }

            Vector3 warp;

            if (warpTime == 0f)
            {
                warp = particleSystemState.Random.NextBetweenPerComponent(rampStart, rampEnd);
            }
            else
            {
                var rampInput = particle.CreationTime;

                if (useCount)
                {
                    rampInput = warpCount++;
                }

                var progress = MathUtils.RemapValClamped(rampInput, warpStartTime, warpStartTime + warpTime, 0f, 1f);
                warp = Vector3.Lerp(rampStart, rampEnd, progress);
            }

            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);
            var transform = Matrix4x4.CreateFromQuaternion(controlPoint.GetRotation())
                * Matrix4x4.CreateTranslation(controlPoint.Position);

            if (Matrix4x4.Invert(transform, out var inverse))
            {
                var local = Vector3.Transform(particle.Position, inverse);
                particle.Position = Vector3.Transform(local * warp, transform);

                var localPrevious = Vector3.Transform(particle.PositionPrevious, inverse);
                particle.PositionPrevious = Vector3.Transform(localPrevious * warp * prevPosScale, transform);
            }

            if (radiusComponent >= 0)
            {
                particle.Radius *= warp.GetComponent(radiusComponent);
            }

            return particle;
        }
    }
}
