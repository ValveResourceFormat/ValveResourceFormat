using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
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
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider numToAssign = new LiteralNumberProvider(100f);
        private readonly INumberProvider cohesionStrength = new LiteralNumberProvider(1f);
        private readonly float tolerance;
        private readonly bool loop = true;
        private readonly bool useParticleCount;
        private readonly ParticlePathParameters pathParams;

        private int slotIndex;
        private int slotStep;
        private Vector3 lastStartPosition;
        private Vector3 lastEndPosition;

        public MaintainSequentialPath(ParticleDefinitionParser parse) : base(parse)
        {
            maxDistance = parse.NumberProvider("m_fMaxDistance", maxDistance);
            numToAssign = parse.NumberProvider("m_flNumToAssign", numToAssign);
            cohesionStrength = parse.NumberProvider("m_flCohesionStrength", cohesionStrength);
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
            lastStartPosition = new Vector3(float.MaxValue);
            lastEndPosition = new Vector3(float.MaxValue);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (tolerance > 0f)
            {
                var startNow = particleSystemState.GetControlPoint(pathParams.StartControlPointNumber).Position;
                var endNow = particleSystemState.GetControlPoint(pathParams.EndControlPointNumber).Position;

                // The raw tolerance value is compared against the squared control point movement.
                if (Vector3.DistanceSquared(startNow, lastStartPosition) < tolerance
                    && Vector3.DistanceSquared(endNow, lastEndPosition) < tolerance)
                {
                    return;
                }

                lastStartPosition = startNow;
                lastEndPosition = endNow;
            }

            var assignCount = useParticleCount ? particles.Count : numToAssign.NextNumber(particleSystemState);
            var parameterStep = assignCount <= 1f ? 0f : 1f / (assignCount - 1f);
            var maxOffset = maxDistance.NextNumber(particleSystemState);
            var offsetRetention = 1f - cohesionStrength.NextNumber(particleSystemState);

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

                particle.Position = PullTowards(particle.Position, target, maxOffset, offsetRetention);
                particle.PositionPrevious = PullTowards(particle.PositionPrevious, target, maxOffset, offsetRetention);

                slotIndex += slotStep;
            }
        }

        private static Vector3 PullTowards(Vector3 position, Vector3 target, float maxDistance, float offsetRetention)
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
