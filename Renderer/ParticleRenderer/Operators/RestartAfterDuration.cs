namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Restarts the particle system after a configurable duration.
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

        private float childInterval = -1f;
        private float childIntervalStart;

        public RestartAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            durationMin = parse.Float("m_flDurationMin", durationMin);
            durationMax = parse.Float("m_flDurationMax", durationMax);
            controlPoint = parse.Int32("m_nCP", controlPoint);
            controlPointField = parse.Int32("m_nCPField", controlPointField);
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            onlyChildren = parse.Boolean("m_bOnlyChildren", onlyChildren);
        }

        public override void Reset()
        {
            childInterval = -1f;
            childIntervalStart = 0f;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (particleSystemState.EndEarly)
            {
                return;
            }

            if (onlyChildren)
            {
                OperateOnChildren(particleSystemState);
                return;
            }

            particleSystemState.SetRestartTime(ScaleDuration(SampleDuration(), particleSystemState));
        }

        private void OperateOnChildren(ParticleSystemRenderState particleSystemState)
        {
            if (childInterval < 0f)
            {
                childInterval = SampleDuration();
                childIntervalStart = particleSystemState.Age;
            }

            if (particleSystemState.Age <= childIntervalStart + ScaleDuration(childInterval, particleSystemState))
            {
                return;
            }

            particleSystemState.Data?.RestartChildrenInGroup(childGroupId, 0f);

            // The interval is re-rolled only once a restart has actually fired, not every frame
            childIntervalStart = particleSystemState.Age;
            childInterval = SampleDuration();
        }

        private float SampleDuration()
        {
            return durationMax != durationMin
                ? ParticleSystemRenderState.RandomFloat(durationMin, durationMax)
                : durationMin;
        }

        private float ScaleDuration(float duration, ParticleSystemRenderState particleSystemState)
        {
            if (controlPoint < 0 || controlPointField < 0)
            {
                return duration;
            }

            var point = particleSystemState.GetControlPoint(controlPoint);
            return duration * point.Position.GetComponent(controlPointField);
        }
    }
}
