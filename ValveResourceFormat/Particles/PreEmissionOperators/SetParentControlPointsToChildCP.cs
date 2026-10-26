namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Hands this system's control points down to the child systems tagged with a group id. Each matching
    /// child receives the next source control point in turn, and they all write it to the same destination
    /// index on their own system and the systems below it, so siblings can hold different values at that
    /// index.
    /// </summary>
    /// <remarks>
    /// At most <c>m_nNumControlPoints</c> are handed out, and never more than this system has children
    /// of any group.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetParentControlPointsToChildCP">C_OP_SetParentControlPointsToChildCP</seealso>
    class SetParentControlPointsToChildCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId;
        private readonly int childControlPoint;
        private readonly int numControlPoints = 1;
        private readonly int firstSourcePoint;
        private readonly bool setOrientation;

        public SetParentControlPointsToChildCP(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            childControlPoint = parse.Int32("m_nChildControlPoint", childControlPoint);
            numControlPoints = parse.Int32("m_nNumControlPoints", numControlPoints);
            firstSourcePoint = parse.Int32("m_nFirstSourcePoint", firstSourcePoint);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var children = particleSystemState.Children;
            var remaining = Math.Min(numControlPoints, children.Count);
            var source = firstSourcePoint;

            foreach (var child in children)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (!ParticleSystemSimulation.IsInGroup(child, childGroupId))
                {
                    continue;
                }

                var sourcePoint = particleSystemState.GetControlPoint(source);
                var destination = child.GetControlPoint(childControlPoint);

                destination.Position = sourcePoint.Position;

                if (setOrientation)
                {
                    destination.SetOrientation(sourcePoint.Forward, sourcePoint.Left, sourcePoint.Up);
                }

                remaining--;
                source++;
            }
        }
    }
}
