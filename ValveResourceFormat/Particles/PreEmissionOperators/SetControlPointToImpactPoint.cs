using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Traces into the world from a control point and moves another control point to the hit, pulled
    /// back along the trace by <c>m_flOffset</c> and facing along the hit normal. The trace runs along
    /// <c>m_vecTraceDir</c>, or the input control point's forward axis when that is zero, or along
    /// whichever of the six axes hits nearest with <c>m_bTraceToClosestSurface</c>. With
    /// <c>m_bSetToEndpoint</c> a single trace that misses still moves the control point, to its end.
    /// Otherwise a miss along an authored direction only turns the output control point to face back
    /// along it. The trace repeats every <c>m_flUpdateRate</c> seconds, or only once when that is negative.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointToImpactPoint">C_OP_SetControlPointToImpactPoint</seealso>
    class SetControlPointToImpactPoint : ParticleFunctionPreEmissionOperator
    {
        private static readonly Vector3[] Axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

        private readonly int outputControlPoint = 1;
        private readonly int inputControlPoint = 1;
        private readonly float updateRate = 0.5f;
        private readonly INumberProvider traceLength = new LiteralNumberProvider(1024f);
        private readonly float startOffset;
        private readonly float offset;
        private readonly Vector3 traceDirection;
        private readonly bool setToEndpoint;
        private readonly bool traceToClosestSurface;

        private float nextUpdateTime;

        public SetControlPointToImpactPoint(ParticleDefinitionParser parse) : base(parse)
        {
            outputControlPoint = parse.ControlPoint("m_nCPOut", outputControlPoint);
            inputControlPoint = parse.ControlPoint("m_nCPIn", inputControlPoint);
            updateRate = parse.Float("m_flUpdateRate", updateRate);
            traceLength = parse.CollectionNumberProvider("m_flTraceLength", traceLength);
            startOffset = parse.Float("m_flStartOffset", startOffset);
            offset = parse.Float("m_flOffset", offset);
            traceDirection = MathUtils.SafeNormalize(parse.Vector3("m_vecTraceDir", traceDirection));
            setToEndpoint = parse.Boolean("m_bSetToEndpoint", setToEndpoint);
            traceToClosestSurface = parse.Boolean("m_bTraceToClosestSurface", traceToClosestSurface);
        }

        public override void Reset()
        {
            nextUpdateTime = -1f;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var time = particleSystemState.Age;

            if (nextUpdateTime > time)
            {
                return;
            }

            nextUpdateTime = updateRate >= 0f ? time + updateRate : float.MaxValue;

            var input = particleSystemState.GetControlPoint(inputControlPoint);
            var direction = traceDirection == Vector3.Zero ? input.Forward : traceDirection;
            var length = traceLength.NextNumber(particleSystemState);
            var collision = particleSystemState.Collision;

            bool hit;
            Vector3 hitPosition;
            Vector3 hitNormal;

            if (traceToClosestSurface)
            {
                hit = false;
                hitPosition = Vector3.Zero;
                hitNormal = Vector3.Zero;
                var nearest = float.MaxValue;

                foreach (var axis in Axes)
                {
                    var start = (axis * startOffset) + input.Position;

                    if (!collision.TraceRay(start, (axis * length) + start, out var position, out var normal, out _))
                    {
                        continue;
                    }

                    var distance = Vector3.DistanceSquared(position, input.Position);

                    if (distance < nearest)
                    {
                        nearest = distance;
                        hit = true;
                        hitPosition = position;
                        hitNormal = normal;
                        direction = axis;
                    }
                }

                if (!hit)
                {
                    if (!setToEndpoint)
                    {
                        SetFacingAway(particleSystemState);
                    }

                    return;
                }
            }
            else
            {
                var start = (direction * startOffset) + input.Position;
                hit = collision.TraceRay(start, (direction * length) + start, out hitPosition, out hitNormal, out _);
            }

            if (!hit && !setToEndpoint)
            {
                SetFacingAway(particleSystemState);
                return;
            }

            var output = particleSystemState.GetControlPoint(outputControlPoint);
            output.Position = (direction * -offset) + hitPosition;

            var (left, up) = ParticleMath.VectorVectors(hitNormal);
            output.SetOrientation(hitNormal, left, up);
        }

        private void SetFacingAway(ParticleSystemState particleSystemState)
        {
            if (traceDirection != Vector3.Zero)
            {
                particleSystemState.SetControlPointOrientation(outputControlPoint, -traceDirection);
            }
        }
    }
}
