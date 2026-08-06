namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Hands this system's control points down to the child systems tagged with a group id. Each matching
    /// child receives the next source control point in turn, and they all write it to the same destination
    /// index on their own system, so siblings can hold different values at that index.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetParentControlPointsToChildCP">C_OP_SetParentControlPointsToChildCP</seealso>
    class SetParentControlPointsToChildCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId;
        private readonly int childControlPoint;
        private readonly int numControlPoints = 1;
        private readonly int firstSourcePoint;
        private readonly bool setOrientation;

        private readonly List<ParticleSystemRenderState> matchingChildren = [];

        public SetParentControlPointsToChildCP(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            childControlPoint = parse.Int32("m_nChildControlPoint", childControlPoint);
            numControlPoints = parse.Int32("m_nNumControlPoints", numControlPoints);
            firstSourcePoint = parse.Int32("m_nFirstSourcePoint", firstSourcePoint);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (particleSystemState.Data == null)
            {
                return;
            }

            matchingChildren.Clear();
            particleSystemState.Data.CollectChildStatesInGroup(childGroupId, matchingChildren);

            var served = Math.Min(numControlPoints, matchingChildren.Count);

            for (var i = 0; i < served; i++)
            {
                var source = particleSystemState.GetControlPoint(firstSourcePoint + i);
                var destination = matchingChildren[i].OverrideControlPoint(childControlPoint);

                destination.Position = source.Position;

                if (setOrientation)
                {
                    destination.Orientation = source.Orientation;
                    destination.Rotation = source.Rotation;
                }
            }
        }
    }
}
