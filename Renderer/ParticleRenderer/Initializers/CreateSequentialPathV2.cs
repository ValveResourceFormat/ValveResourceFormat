using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places successive particles along a Bezier path using one global parameter over the whole
    /// control point span, supporting reversed spans, exact endpoint landing, and closed-loop seam
    /// deduplication. The running position carries over between emissions.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateSequentialPathV2">C_INIT_CreateSequentialPathV2</seealso>
    class CreateSequentialPathV2 : ParticleFunctionInitializer
    {
        /// <summary>Distance from a span endpoint within which the running parameter snaps to it.</summary>
        private const float PathParameterEpsilon = 1e-5f;

        private readonly INumberProvider maxDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider numToAssign = new LiteralNumberProvider(100f);
        private readonly bool loop = true;
        private readonly bool cpPairs;
        private readonly bool saveOffset;
        private readonly ParticlePathParameters pathParams;

        private readonly int spanLength;
        private readonly int spanDirection;

        private float pathParameter;
        private float parameterStep;
        private float cachedNumToAssign = float.NaN;

        public CreateSequentialPathV2(ParticleDefinitionParser parse) : base(parse)
        {
            maxDistance = parse.NumberProvider("m_fMaxDistance", maxDistance);
            numToAssign = parse.NumberProvider("m_flNumToAssign", numToAssign);
            loop = parse.Boolean("m_bLoop", loop);
            cpPairs = parse.Boolean("m_bCPPairs", cpPairs);
            saveOffset = parse.Boolean("m_bSaveOffset", saveOffset);
            pathParams = new ParticlePathParameters(parse);

            spanDirection = pathParams.EndControlPointNumber >= pathParams.StartControlPointNumber ? 1 : -1;

            // With pairs enabled the span keeps its raw length even when it is 0 or 1; only the pairs
            // flag is dropped, and a start==end path stays degenerate.
            if (cpPairs)
            {
                spanLength = Math.Abs(pathParams.EndControlPointNumber - pathParams.StartControlPointNumber);
                if (spanLength <= 1)
                {
                    cpPairs = false;
                }
            }
            else
            {
                spanLength = 1;
            }
        }

        public override void Reset()
        {
            pathParameter = 0f;
            parameterStep = 0f;
            cachedNumToAssign = float.NaN;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var assignCount = numToAssign.NextNumber(particleSystemState);
            if (assignCount != cachedNumToAssign)
            {
                cachedNumToAssign = assignCount;
                parameterStep = assignCount > 1f ? 1f / (assignCount - 1f) : 0f;

                if (cpPairs && spanLength > 1)
                {
                    parameterStep *= spanLength;
                }
            }

            var endpointsDistinct = true;
            if (cpPairs && loop)
            {
                var startPos = particleSystemState.GetControlPoint(pathParams.StartControlPointNumber).Position;
                var endPos = particleSystemState.GetControlPoint(pathParams.EndControlPointNumber).Position;
                endpointsDistinct = MathF.Abs(startPos.X - endPos.X) > 0.001f
                    || MathF.Abs(startPos.Y - endPos.Y) > 0.001f
                    || MathF.Abs(startPos.Z - endPos.Z) > 0.001f;
            }

            var t = pathParameter;

            int segmentStart;
            int segmentEnd;

            if (!cpPairs)
            {
                segmentStart = pathParams.StartControlPointNumber;
                segmentEnd = pathParams.EndControlPointNumber;
            }
            else
            {
                segmentStart = pathParams.StartControlPointNumber + (int)t * spanDirection;
                segmentEnd = segmentStart + spanDirection;
                t -= (int)t;

                if (spanDirection * (spanDirection + segmentStart - pathParams.EndControlPointNumber) >= 1)
                {
                    segmentEnd = pathParams.EndControlPointNumber;
                    segmentStart = pathParams.EndControlPointNumber - spanDirection;
                    t = 1f;
                }
            }

            var segment = pathParams.WithControlPoints(segmentStart, segmentEnd);
            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, segment, particle.CreationTime);

            var position = ParticlePath.Evaluate(start, mid, end, t);

            if (saveOffset)
            {
                particle.HitboxOffsetPosition = new Vector3(t, segmentStart, segmentEnd);
            }

            var jitter = maxDistance.NextNumber(ref particle, particleSystemState);
            position += particleSystemState.Random.NextBetweenPerComponent(new Vector3(-jitter), new Vector3(jitter));

            particle.Position = position;
            particle.PositionPrevious = position;

            var previousParameter = pathParameter;
            pathParameter += parameterStep;
            var advanced = pathParameter;

            if (parameterStep > 0f)
            {
                if (MathF.Abs(advanced - spanLength) < PathParameterEpsilon)
                {
                    advanced = spanLength;
                    pathParameter = advanced;
                }
                else if (MathF.Abs(advanced) < PathParameterEpsilon)
                {
                    advanced = 0f;
                    pathParameter = 0f;
                }
            }

            if (advanced > spanLength || advanced < 0f)
            {
                var wrapped = parameterStep <= 0f ? -advanced : advanced - spanLength;

                if (loop)
                {
                    pathParameter = wrapped;

                    if (endpointsDistinct && previousParameter == spanLength)
                    {
                        pathParameter = 0f;
                    }
                }
                else
                {
                    if (parameterStep >= 0f)
                    {
                        wrapped = spanLength - wrapped;
                    }

                    pathParameter = wrapped;
                    parameterStep = -parameterStep;
                }
            }

            return particle;
        }
    }
}
