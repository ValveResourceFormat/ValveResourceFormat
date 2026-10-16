namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Starts one child system of a group for every particle this system holds, beginning at the
    /// group child <c>m_nFirstChild</c> and capped by <c>m_nNumChildrenToEnable</c> unless that is
    /// negative. With <c>m_bDisableChildren</c> the children outside that window are stopped again,
    /// playing their endcap with <c>m_bPlayEndcapOnStop</c> or removed at once with
    /// <c>m_bDestroyImmediately</c>.
    /// </summary>
    /// <remarks>
    /// The group is held back from the start. A child that finishes waits again but still counts as
    /// started, so it is only replaced once another one is stopped. The children held at the start
    /// are counted from <c>m_nFirstChild</c> among all children, the ones started among the group.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_EnableChildrenFromParentParticleCount">C_OP_EnableChildrenFromParentParticleCount</seealso>
    class EnableChildrenFromParentParticleCount : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId;
        private readonly int firstChild;
        private readonly INumberProvider numChildrenToEnable = new LiteralNumberProvider(-1f);
        private readonly bool disableChildren;
        private readonly bool playEndcapOnStop = true;
        private readonly bool destroyImmediately;

        private bool childrenHeld;
        private int enabledCount;

        public EnableChildrenFromParentParticleCount(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            firstChild = parse.Int32("m_nFirstChild", firstChild);
            numChildrenToEnable = parse.NumberProvider("m_nNumChildrenToEnable", numChildrenToEnable);
            disableChildren = parse.Boolean("m_bDisableChildren", disableChildren);
            playEndcapOnStop = parse.Boolean("m_bPlayEndcapOnStop", playEndcapOnStop);
            destroyImmediately = parse.Boolean("m_bDestroyImmediately", destroyImmediately);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            childrenHeld = false;
            enabledCount = 0;

            foreach (var child in particleSystemState.Data?.Children ?? [])
            {
                if (child.GroupId == childGroupId)
                {
                    child.Hold(finishedWhileHeld: true);
                }
            }
        }

        public override void Restart(ParticleSystemState particleSystemState) => Initialize(particleSystemState);

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (particleSystemState.Data is not { } simulation)
            {
                return;
            }

            var children = simulation.Children;

            if (!childrenHeld)
            {
                for (var index = firstChild; index < children.Count; index++)
                {
                    if (index >= 0 && children[index].GroupId == childGroupId)
                    {
                        children[index].Hold(finishedWhileHeld: true);
                    }
                }

                childrenHeld = true;
            }

            var target = (int)Math.Min(particleSystemState.ParticleCount - firstChild, children.Count);
            var cap = (int)numChildrenToEnable.NextNumber(particleSystemState);

            if (cap >= 0 && cap < target)
            {
                target = cap;
            }

            if (target == 0 || children.Count == 0)
            {
                return;
            }

            var budget = target;
            var groupIndex = 0;

            foreach (var child in children)
            {
                if (child.GroupId != childGroupId)
                {
                    continue;
                }

                if (child.IsFinished())
                {
                    child.Hold(finishedWhileHeld: false);
                }

                var age = child.RenderState.Age;

                if (disableChildren && (groupIndex < firstChild || groupIndex >= target) && age > 0f
                    && (destroyImmediately || !child.RenderState.InEndCap))
                {
                    child.StopEmission(removeAllParticles: destroyImmediately, playEndCap: playEndcapOnStop);

                    if (destroyImmediately)
                    {
                        child.Hold(finishedWhileHeld: false);
                    }

                    enabledCount--;
                }

                if (groupIndex >= firstChild && budget != 0 && enabledCount < target && child.RenderState.Age < 0f)
                {
                    child.RenderState.Age = 0f;
                    budget--;
                    enabledCount++;
                }

                groupIndex++;
            }
        }
    }
}
