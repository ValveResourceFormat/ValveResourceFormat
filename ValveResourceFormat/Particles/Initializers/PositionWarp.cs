namespace ValveResourceFormat.Particles.Initializers
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
            warpMin = parse.CollectionVectorProvider("m_vecWarpMin", warpMin);
            warpMax = parse.CollectionVectorProvider("m_vecWarpMax", warpMax);
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
            scaleControlPointNumber = parse.Int32("m_nScaleControlPointNumber", scaleControlPointNumber);
            radiusComponent = Math.Clamp(parse.Int32("m_nRadiusComponent", radiusComponent), -1, 2);
            warpTime = parse.Float("m_flWarpTime", warpTime);
            warpStartTime = parse.Float("m_flWarpStartTime", warpStartTime);
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            invertWarp = parse.Boolean("m_bInvertWarp", invertWarp);
            useCount = parse.Boolean("m_bUseCount", useCount);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(scaleControlPointNumber);
        }

        public override void Reset()
        {
            warpCount = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Radius.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var rampStart = warpMin.NextVector(particleSystemState);
            var rampEnd = warpMax.NextVector(particleSystemState);

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

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);
            var radii = radiusComponent >= 0 ? run.Floats(ParticleField.Radius) : default;
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);

            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            for (var i = 0; i < run.Length; i++)
            {
                Vector3 warp;

                if (warpTime == 0f)
                {
                    warp = particleSystemState.Random.NextBetweenPerComponentSeededTwice(rampStart, rampEnd);
                }
                else
                {
                    var (rampInput, rampBase) = useCount
                        ? (warpCount++, 0f)
                        : (creationTimes[i], particleSystemState.OperatorTimeBase);

                    var rampStartTime = warpStartTime + rampBase;
                    var progress = MathUtils.RemapValClamped(rampInput, rampStartTime, rampStartTime + warpTime, 0f, 1f);
                    warp = Vector3.Lerp(rampStart, rampEnd, progress);
                }

                var frame = controlPoint.GetBasisAtTime(creationTimes[i]);
                Warp(frame, warp, prevPosScale, ref positions[i], ref previousPositions[i]);

                if (radiusComponent >= 0)
                {
                    radii[i] *= warp.GetComponent(radiusComponent);
                }
            }
        }

        /// <summary>
        /// Scales <paramref name="position"/> by <paramref name="warp"/> and <paramref name="previous"/> by
        /// <paramref name="warp"/> times <paramref name="previousScale"/>, each in the local axes of
        /// <paramref name="frame"/> about its origin.
        /// </summary>
        internal static void Warp(Matrix4x4 frame, Vector3 warp, float previousScale, ref Vector3 position, ref Vector3 previous)
        {
            var origin = frame.Translation;
            var toLocal = Matrix4x4.Transpose(frame with { Translation = Vector3.Zero });

            var local = Vector3.TransformNormal(position - origin, toLocal);
            position = Vector3.Transform(local * warp, frame);

            var localPrevious = Vector3.TransformNormal(previous - origin, toLocal);
            previous = Vector3.Transform(previousScale * warp * localPrevious, frame);
        }
    }
}
