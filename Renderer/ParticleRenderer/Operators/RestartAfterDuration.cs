using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Restarts the particle system after a configurable duration.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RestartAfterDuration">C_OP_RestartAfterDuration</seealso>
    class RestartAfterDuration : ParticleFunctionOperator
    {
        private readonly float durationMin = 1f;
        private readonly float durationMax = 1f;
        private readonly int controlPoint = -1;
        private readonly int controlPointField;
        private readonly int childGroupId = -1;
        private readonly bool onlyChildren;

        public RestartAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            durationMin = parse.Float("m_flDurationMin", durationMin);
            durationMax = parse.Float("m_flDurationMax", durationMax);
            controlPoint = parse.Int32("m_nCP", controlPoint);
            controlPointField = parse.Int32("m_nCPField", controlPointField);
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            onlyChildren = parse.Boolean("m_bOnlyChildren", onlyChildren);

            if (childGroupId >= 0 || onlyChildren)
            {
                Logger.LogWarning(
                    "C_OP_RestartAfterDuration child group support is not implemented. Restart applies to the entire particle system.");
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            if (particleSystemState.EndEarly)
            {
                return;
            }

            var duration = durationMin;
            if (durationMax != durationMin)
            {
                duration = ParticleSystemRenderState.RandomFloat(durationMin, durationMax);
            }

            if (controlPoint >= 0)
            {
                var point = particleSystemState.GetControlPoint(controlPoint);
                duration *= point.Position.GetComponent(controlPointField);
            }

            particleSystemState.SetStopTime(duration, true);
        }
    }
}
