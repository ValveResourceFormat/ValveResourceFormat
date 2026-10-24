namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes an attribute of one particle into a control point of each child of a group, stepping
    /// through the particles from child to child. A vector attribute sets the position, a scalar sets
    /// one component of it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetPerChildControlPointFromAttribute">C_OP_SetPerChildControlPointFromAttribute</seealso>
    class SetPerChildControlPointFromAttribute : ParticleFunctionOperator
    {
        private readonly int childGroupId;
        private readonly int firstControlPoint;
        private readonly int numControlPoints = 1;
        private readonly int particleIncrement = 1;
        private readonly int firstSourcePoint;
        private readonly bool numBasedOnParticleCount;
        private readonly ParticleField attributeToRead = ParticleField.NoneDisabled;
        private readonly int controlPointField;

        public SetPerChildControlPointFromAttribute(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            firstControlPoint = parse.Int32("m_nFirstControlPoint", firstControlPoint);
            numControlPoints = parse.Int32("m_nNumControlPoints", numControlPoints);
            particleIncrement = parse.Int32("m_nParticleIncrement", particleIncrement);
            firstSourcePoint = parse.Int32("m_nFirstSourcePoint", firstSourcePoint);
            numBasedOnParticleCount = parse.Boolean("m_bNumBasedOnParticleCount", numBasedOnParticleCount);
            attributeToRead = parse.ParticleField("m_nAttributeToRead", attributeToRead);
            controlPointField = parse.Int32("m_nCPField", controlPointField);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (attributeToRead == ParticleField.NoneDisabled)
            {
                return;
            }

            var live = particles.Current;
            var limit = numBasedOnParticleCount ? live.Length : numControlPoints;
            var remaining = Math.Min(limit, Math.Min(live.Length - firstSourcePoint, particleSystemState.Children.Count));
            var source = firstSourcePoint;
            var isVector = attributeToRead.FieldType() == "vector";

            foreach (var child in particleSystemState.Children)
            {
                if (remaining <= 0 || (uint)source >= (uint)live.Length)
                {
                    return;
                }

                if (!ParticleSystemSimulation.IsInGroup(child, childGroupId))
                {
                    continue;
                }

                var particle = live[source];

                if (isVector)
                {
                    child.GetControlPoint(firstControlPoint).Position = particle.GetVector(attributeToRead);
                }
                else if (controlPointField is >= 0 and <= 2)
                {
                    child.SetControlPointValueComponent(firstControlPoint, controlPointField, particle.GetScalar(attributeToRead));
                }

                remaining--;
                source += particleIncrement;
            }
        }
    }
}
