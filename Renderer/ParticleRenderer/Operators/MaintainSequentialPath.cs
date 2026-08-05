using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Pulls every particle toward an evenly spaced slot along a Bezier path between control points,
    /// assigning slots in particle order with a running index that loops or ping-pongs. Cohesion
    /// blends between snapping onto the path and preserving each particle's offset, clamped to the
    /// maximum distance.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaintainSequentialPath">C_OP_MaintainSequentialPath</seealso>
    class MaintainSequentialPath : ParticleFunctionOperator
    {
        private readonly float maxDistance;
        private readonly float numToAssign = 100f;
        private readonly float cohesionStrength = 1f;
        private readonly float tolerance;
        private readonly bool loop = true;
        private readonly bool useParticleCount;
        private readonly ParticlePathParameters pathParams;

        private int slotIndex;
        private int slotStep;
        private float parameterStep;
        private Vector3 lastStartPosition;
        private Vector3 lastEndPosition;

        public MaintainSequentialPath(ParticleDefinitionParser parse) : base(parse)
        {
            maxDistance = parse.Float("m_fMaxDistance", maxDistance);
            numToAssign = parse.Float("m_flNumToAssign", numToAssign);
            cohesionStrength = parse.Float("m_flCohesionStrength", cohesionStrength);
            loop = parse.Boolean("m_bLoop", loop);
            useParticleCount = parse.Boolean("m_bUseParticleCount", useParticleCount);
            pathParams = new ParticlePathParameters(parse);

            tolerance = parse.Float("m_flTolerance", 0f);

            Reset();
        }

        public override void Reset()
        {
            slotIndex = 0;
            slotStep = 1;
            parameterStep = numToAssign <= 1f ? 0f : 1f / (numToAssign - 1f);
            lastStartPosition = new Vector3(float.MaxValue);
            lastEndPosition = new Vector3(float.MaxValue);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (tolerance > 0f)
            {
                var startNow = particleSystemState.GetControlPoint(pathParams.StartControlPointNumber).Position;
                var endNow = particleSystemState.GetControlPoint(pathParams.EndControlPointNumber).Position;

                // The raw tolerance value is compared against the squared control point movement.
                if ((startNow - lastStartPosition).LengthSquared() < tolerance
                    && (endNow - lastEndPosition).LengthSquared() < tolerance)
                {
                    return;
                }

                lastStartPosition = startNow;
                lastEndPosition = endNow;
            }

            var assignCount = numToAssign;
            if (useParticleCount)
            {
                assignCount = particles.Count;
                parameterStep = assignCount <= 1f ? 0f : 1f / (assignCount - 1f);
            }

            var offsetRetention = 1f - cohesionStrength;

            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, pathParams, particleSystemState.Age);

            foreach (ref var particle in particles.Current)
            {
                var index = slotIndex;

                if (index >= assignCount || index < 0)
                {
                    if (loop)
                    {
                        index = 0;
                        slotIndex = 0;
                    }
                    else
                    {
                        slotStep = -slotStep;
                        index = Math.Max(Math.Min(index, (int)assignCount - 1), 1);
                        slotIndex = index;
                    }
                }

                var target = ParticlePath.Evaluate(start, mid, end, index * parameterStep);

                particle.Position = PullTowards(particle.Position, target, offsetRetention);
                particle.PositionPrevious = PullTowards(particle.PositionPrevious, target, offsetRetention);

                slotIndex += slotStep;
            }
        }

        private Vector3 PullTowards(Vector3 position, Vector3 target, float offsetRetention)
        {
            var offset = position - target;
            var distance = offset.Length();

            if (distance <= 0f)
            {
                return target;
            }

            return target + (offset / distance) * (MathF.Min(distance, maxDistance) * offsetRetention);
        }
    }
}
