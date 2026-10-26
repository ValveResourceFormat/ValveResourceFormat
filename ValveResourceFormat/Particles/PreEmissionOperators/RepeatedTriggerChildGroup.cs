namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Starts one random waiting child system of a group every <c>m_flClusterRefireTime</c> seconds,
    /// in clusters of <c>m_flClusterSize</c> plus one starts separated by <c>m_flClusterCooldown</c>.
    /// The group is held back from the start, and a child goes back to waiting once it has finished.
    /// </summary>
    /// <remarks>
    /// The refire time passes even when no child is waiting. With <c>m_bLimitChildCount</c> only as
    /// many children as this system has particles are candidates. Nothing starts while emission is
    /// stopped.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RepeatedTriggerChildGroup">C_OP_RepeatedTriggerChildGroup</seealso>
    class RepeatedTriggerChildGroup : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId = 1;
        private readonly INumberProvider clusterRefireTime = new LiteralNumberProvider(0f);
        private readonly INumberProvider clusterSize = new LiteralNumberProvider(0f);
        private readonly INumberProvider clusterCooldown = new LiteralNumberProvider(0f);
        private readonly bool limitChildCount;

        private readonly List<ParticleSystemSimulation> candidates = [];

        private bool initialized;
        private int currentClusterSize;
        private int firedInCluster;
        private float nextFireTime;

        public RepeatedTriggerChildGroup(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            clusterRefireTime = parse.NumberProvider("m_flClusterRefireTime", clusterRefireTime);
            clusterSize = parse.NumberProvider("m_flClusterSize", clusterSize);
            clusterCooldown = parse.NumberProvider("m_flClusterCooldown", clusterCooldown);
            limitChildCount = parse.Boolean("m_bLimitChildCount", limitChildCount);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            initialized = false;
            currentClusterSize = 0;
            firedInCluster = 0;
            nextFireTime = -1f;
            particleSystemState.Data?.HoldChildrenInGroup(childGroupId);
        }

        public override void Restart(ParticleSystemState particleSystemState) => Initialize(particleSystemState);

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (particleSystemState.EmissionStopped || particleSystemState.Data is not { } simulation)
            {
                return;
            }

            // The refire time is only evaluated once the fire time is reached, and the cooldown when a cluster ends
            var size = clusterSize.NextNumber(particleSystemState);

            if (!initialized)
            {
                simulation.HoldChildrenInGroup(childGroupId);
                currentClusterSize = (int)size;
                initialized = true;
            }

            var children = simulation.Children;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (child.GroupId == childGroupId && child.IsFinished())
                {
                    child.Hold(finishedWhileHeld: true);
                }
            }

            if (particleSystemState.Age < nextFireTime)
            {
                return;
            }

            candidates.Clear();

            for (var i = 0; i < children.Count; i++)
            {
                if (limitChildCount && candidates.Count >= particleSystemState.ParticleCount)
                {
                    break;
                }

                var child = children[i];

                if (child.GroupId == childGroupId && child.RenderState.Age < 0f)
                {
                    candidates.Add(child);
                }
            }

            if (candidates.Count > 0)
            {
                var chosen = candidates[particleSystemState.Random.NextIndex(candidates.Count)];
                chosen.RenderState.Age = 0f;
                chosen.Restart();
                firedInCluster++;
            }

            nextFireTime = particleSystemState.Age + clusterRefireTime.NextNumber(particleSystemState);

            if (firedInCluster > currentClusterSize)
            {
                nextFireTime += clusterCooldown.NextNumber(particleSystemState);
                firedInCluster = 0;
                currentClusterSize = (int)size;
            }
        }
    }
}
