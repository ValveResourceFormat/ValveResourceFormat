namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Runs a window of <c>m_nNumChildrenToEnable</c> child systems of a group, starting at the
    /// group child <c>m_nFirstChild</c>, and stops the rest of the group, playing their endcap with
    /// <c>m_bPlayEndcapOnStop</c> or removing them at once with <c>m_bDestroyImmediately</c>.
    /// </summary>
    /// <remarks>
    /// The group is held back from the start, and the window is applied again only when the inputs
    /// move it; in between, children that finish go back to waiting.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SelectivelyEnableChildren">C_OP_SelectivelyEnableChildren</seealso>
    class SelectivelyEnableChildren : ParticleFunctionPreEmissionOperator
    {
        private readonly INumberProvider childGroupId = new LiteralNumberProvider(0f);
        private readonly INumberProvider firstChild = new LiteralNumberProvider(0f);
        private readonly INumberProvider numChildrenToEnable = new LiteralNumberProvider(1f);
        private readonly bool playEndcapOnStop = true;
        private readonly bool destroyImmediately;

        private bool initialized;
        private int lastNumEnabled;
        private int lastFirstChild;
        private int groupChildCount;

        public SelectivelyEnableChildren(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.NumberProvider("m_nChildGroupID", childGroupId);
            firstChild = parse.NumberProvider("m_nFirstChild", firstChild);
            numChildrenToEnable = parse.NumberProvider("m_nNumChildrenToEnable", numChildrenToEnable);
            playEndcapOnStop = parse.Boolean("m_bPlayEndcapOnStop", playEndcapOnStop);
            destroyImmediately = parse.Boolean("m_bDestroyImmediately", destroyImmediately);
        }

        public override void Reset()
        {
            initialized = false;
            lastNumEnabled = 0;
            lastFirstChild = 0;
            groupChildCount = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (particleSystemState.Data is not { } simulation)
            {
                return;
            }

            var children = simulation.Children;
            var group = (int)childGroupId.NextNumber(particleSystemState);
            var first = (int)firstChild.NextNumber(particleSystemState);
            var count = Math.Max(0, (int)numChildrenToEnable.NextNumber(particleSystemState));

            if (!initialized)
            {
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];

                    if (child.GroupId == group)
                    {
                        groupChildCount++;
                        child.Hold(finishedWhileHeld: true);
                    }
                }

                initialized = true;
            }

            var enable = Math.Min(count, groupChildCount - first);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (child.GroupId == group && child.IsFinished())
                {
                    child.Hold(finishedWhileHeld: true);
                }
            }

            if (enable == lastNumEnabled && first == lastFirstChild)
            {
                return;
            }

            var index = 0;
            var enabled = 0;
            var remaining = enable;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (child.GroupId != group)
                {
                    continue;
                }

                var inWindow = index >= first;

                if (!(inWindow && enabled < enable) && child.RenderState.Age > 0f
                    && (destroyImmediately || !child.RenderState.InEndCap))
                {
                    child.StopEmission(removeAllParticles: destroyImmediately, playEndCap: playEndcapOnStop);

                    if (destroyImmediately)
                    {
                        child.Hold(finishedWhileHeld: true);
                    }
                }

                if (inWindow && remaining != 0)
                {
                    if (child.RenderState.Age < 0f)
                    {
                        child.RenderState.Age = 0f;
                        child.Restart();
                        child.Start();
                    }

                    enabled++;
                    remaining--;
                }

                index++;
            }

            lastNumEnabled = enable;
            lastFirstChild = first;
        }
    }
}
