using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places successive particles at evenly spaced parameters along a Bezier path between control
    /// points, walking sequential control point pairs when enabled, looping or ping-ponging at the
    /// ends. The running position carries over between emissions.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateSequentialPath">C_INIT_CreateSequentialPath</seealso>
    class CreateSequentialPath : ParticleFunctionInitializer
    {
        private readonly float maxDistance;
        private readonly float numToAssign = 100f;
        private readonly bool loop = true;
        private readonly bool cpPairs;
        private readonly bool saveOffset;
        private readonly ParticlePathParameters pathParams;

        private float pathParameter;
        private float wrapDirection;
        private float controlPoint;
        private float parameterStep;
        private float controlPointStep;

        public CreateSequentialPath(ParticleDefinitionParser parse) : base(parse)
        {
            maxDistance = parse.Float("m_fMaxDistance", maxDistance);
            numToAssign = parse.Float("m_flNumToAssign", numToAssign);
            loop = parse.Boolean("m_bLoop", loop);
            cpPairs = parse.Boolean("m_bCPPairs", cpPairs);
            saveOffset = parse.Boolean("m_bSaveOffset", saveOffset);
            pathParams = new ParticlePathParameters(parse);

            Reset();
        }

        public override void Reset()
        {
            pathParameter = 0f;
            wrapDirection = -1f;
            controlPoint = cpPairs ? pathParams.StartControlPointNumber : 0f;

            var step = numToAssign <= 1f ? 0f : 1f / (numToAssign - 1f);
            parameterStep = step;
            controlPointStep = 0f;

            var span = pathParams.EndControlPointNumber - pathParams.StartControlPointNumber;
            if (cpPairs && span > 1 && numToAssign > 1f)
            {
                parameterStep = span * step;
                controlPointStep = span * step;
            }
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var t = pathParameter;
            var cp = controlPoint;

            int segmentStart;
            int segmentEnd;

            if (!cpPairs)
            {
                segmentStart = pathParams.StartControlPointNumber;
                segmentEnd = pathParams.EndControlPointNumber;
            }
            else
            {
                if ((int)cp > pathParams.EndControlPointNumber || (int)cp < pathParams.StartControlPointNumber)
                {
                    if (loop)
                    {
                        cp = pathParams.StartControlPointNumber;
                        controlPoint = cp;
                    }
                    else
                    {
                        controlPointStep = -controlPointStep;
                        parameterStep = -parameterStep;
                        wrapDirection = -wrapDirection;
                        cp += 2f * controlPointStep;
                        controlPoint = cp;
                        t += 2f * parameterStep;
                        pathParameter = t;
                    }
                }

                segmentStart = (int)cp;
                segmentEnd = segmentStart == pathParams.EndControlPointNumber ? segmentStart : segmentStart + 1;
            }

            if (t > 1.0000001f || t < 0f)
            {
                if (loop || cpPairs)
                {
                    t += wrapDirection;
                    pathParameter = t;
                }
                else
                {
                    parameterStep = -parameterStep;
                    wrapDirection = -wrapDirection;
                    t += 2f * parameterStep;
                    pathParameter = t;
                }
            }

            var segment = pathParams.WithControlPoints(segmentStart, segmentEnd);
            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, segment, particle.CreationTime);

            var position = ParticlePath.Evaluate(start, mid, end, t);

            if (saveOffset)
            {
                particle.HitboxOffsetPosition = new Vector3(t, segmentStart, segmentEnd);
            }

            position += particleSystemState.Random.NextBetweenPerComponent(new Vector3(-maxDistance), new Vector3(maxDistance));

            particle.Position = position;
            particle.PositionPrevious = position;

            pathParameter += parameterStep;
            controlPoint += controlPointStep;

            return particle;
        }
    }
}
