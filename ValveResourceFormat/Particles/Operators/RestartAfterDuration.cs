namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Restarts the particle system, or only its children of one group, each time a random duration
    /// has passed since the last restart. The duration is optionally scaled by a control point
    /// component, read every step. Nothing restarts while the system is stopped.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RestartAfterDuration">C_OP_RestartAfterDuration</seealso>
    class RestartAfterDuration : ParticleFunctionOperator
    {
        private readonly float durationMin;
        private readonly float durationMax = 1f;
        private readonly int controlPoint = -1;
        private readonly int controlPointField;
        private readonly int childGroupId;
        private readonly bool onlyChildren;

        private float lastRestartTime;
        private float duration;

        public RestartAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            durationMin = parse.Float("m_flDurationMin", durationMin);
            durationMax = parse.Float("m_flDurationMax", durationMax);
            controlPoint = Math.Clamp(parse.Int32("m_nCP", controlPoint), -1, ParticleSystemState.ControlPointCount);
            controlPointField = Math.Clamp(parse.Int32("m_nCPField", controlPointField), -1, 2);
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            onlyChildren = parse.Boolean("m_bOnlyChildren", onlyChildren);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(controlPoint, controlPointField);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            lastRestartTime = 0f;
            duration = particleSystemState.Random.NextBetween(durationMin, durationMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particleSystemState.EmissionStopped)
            {
                return;
            }

            var scaled = duration;

            if (controlPoint > -1 && controlPointField > -1)
            {
                scaled *= particleSystemState.GetControlPoint(controlPoint).Position.GetComponent(controlPointField);
            }

            if (particleSystemState.Age <= scaled + lastRestartTime)
            {
                return;
            }

            if (onlyChildren)
            {
                particleSystemState.Data?.RestartChildrenInGroup(childGroupId);
            }
            else
            {
                particleSystemState.Data?.Restart();
            }

            lastRestartTime = particleSystemState.Age;
            duration = particleSystemState.Random.NextBetween(durationMin, durationMax);
        }
    }
}
