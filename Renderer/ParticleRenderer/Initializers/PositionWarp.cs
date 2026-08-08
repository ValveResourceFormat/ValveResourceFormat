namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Scales each particle's spawn position relative to a control point by a warp vector between a
    /// minimum and a maximum. The vector is drawn per component at random, or, once a warp time is
    /// given, ramped from the minimum to the maximum across the warp window without any random draw.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionWarp">C_INIT_PositionWarp</seealso>
    class PositionWarp : ParticleFunctionInitializer
    {
        private readonly IVectorProvider warpMin = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider warpMax = new LiteralVectorProvider(Vector3.One);
        private readonly int controlPointNumber;
        private readonly float warpTime;
        private readonly float warpStartTime;
        private readonly bool useCount;

        /// <summary>How many particles the ramp has been driven over, when it runs on a count.</summary>
        private int warpCount;

        public PositionWarp(ParticleDefinitionParser parse) : base(parse)
        {
            warpMin = parse.VectorProvider("m_vecWarpMin", warpMin);
            warpMax = parse.VectorProvider("m_vecWarpMax", warpMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            warpTime = parse.Float("m_flWarpTime", warpTime);
            warpStartTime = parse.Float("m_flWarpStartTime", warpStartTime);
            useCount = parse.Boolean("m_bUseCount", useCount);
        }

        public override void Reset()
        {
            warpCount = 0;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var min = warpMin.NextVector(ref particle, particleSystemState);
            var max = warpMax.NextVector(ref particle, particleSystemState);

            Vector3 warp;

            if (warpTime == 0f)
            {
                warp = particleSystemState.NextRandomBetweenPerComponent(min, max);
            }
            else
            {
                var rampInput = particle.CreationTime;

                if (useCount)
                {
                    rampInput = warpCount++;
                }

                var progress = MathUtils.RemapValClamped(rampInput, warpStartTime, warpStartTime + warpTime, 0f, 1f);
                warp = Vector3.Lerp(min, max, progress);
            }

            var origin = particleSystemState.GetControlPoint(controlPointNumber).Position;
            particle.Position = origin + ((particle.Position - origin) * warp);

            return particle;
        }
    }
}
