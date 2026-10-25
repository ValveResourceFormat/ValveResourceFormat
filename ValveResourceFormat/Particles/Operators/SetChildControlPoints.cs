namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Places a run of control points on each child of a group, one per particle of this system,
    /// optionally turned to match the particle. Each child gets the same points, on its own copy and
    /// those of the systems below it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetChildControlPoints">C_OP_SetChildControlPoints</seealso>
    class SetChildControlPoints : ParticleFunctionOperator
    {
        private readonly int childGroupId;
        private readonly int firstControlPoint;
        private readonly int numControlPoints = 1;
        private readonly INumberProvider firstSourcePoint = new LiteralNumberProvider(0f);
        private readonly bool reverse;
        private readonly bool setOrientation;
        private readonly ParticleOrientationType orientationType = ParticleOrientationType.PARTICLE_ORIENTATION_VELOCITY;

        private readonly List<ParticleSystemState> matchingChildren = [];

        public SetChildControlPoints(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            var authoredFirstControlPoint = parse.Int32("m_nFirstControlPoint", firstControlPoint);
            firstControlPoint = Math.Clamp(authoredFirstControlPoint, 0, ParticleSystemState.MaxControlPoint);
            numControlPoints = Math.Min(parse.Int32("m_nNumControlPoints", numControlPoints), ParticleSystemState.ControlPointCount - Math.Max(authoredFirstControlPoint, 0));
            firstSourcePoint = parse.NumberProvider("m_nFirstSourcePoint", firstSourcePoint);
            reverse = parse.Boolean("m_bReverse", reverse);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            orientationType = parse.Enum("m_nOrientation", orientationType);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particleSystemState.Data == null)
            {
                return;
            }

            matchingChildren.Clear();
            particleSystemState.Data.CollectChildStatesInGroup(childGroupId, matchingChildren);

            if (matchingChildren.Count == 0)
            {
                return;
            }

            var live = particles.Current;
            var first = (int)firstSourcePoint.NextNumber(particleSystemState);
            var count = Math.Min(live.Length - first, numControlPoints);

            foreach (var child in matchingChildren)
            {
                for (var i = 0; i < count; i++)
                {
                    var index = first + i;

                    if (reverse)
                    {
                        index = Math.Max(live.Length - index - 1, 0);
                    }

                    if ((uint)index >= (uint)live.Length)
                    {
                        break;
                    }

                    var controlPoint = child.GetControlPoint(firstControlPoint + i);
                    controlPoint.Position = live.ReadVector(ParticleField.Position, index);

                    if (setOrientation)
                    {
                        SetParticleOrientation(controlPoint, live, index);
                    }
                }
            }
        }

        /// <summary>Turns the control point the way the authored method takes from a particle, when it gives a way.</summary>
        private void SetParticleOrientation(ControlPoint controlPoint, ParticleSpan live, int index)
        {
            switch (orientationType)
            {
                case ParticleOrientationType.PARTICLE_ORIENTATION_VELOCITY:
                    SetControlPointsToParticle.OrientToVelocity(controlPoint, live, index);
                    break;

                case ParticleOrientationType.PARTICLE_ORIENTATION_NORMAL:
                    SetControlPointsToParticle.OrientToDirection(controlPoint, live.ReadVector(ParticleField.Normal, index));
                    break;

                case ParticleOrientationType.PARTICLE_ORIENTATION_ROTATION:
                    SetControlPointsToParticle.OrientToRotations(controlPoint, live, index);
                    break;
            }
        }
    }
}
