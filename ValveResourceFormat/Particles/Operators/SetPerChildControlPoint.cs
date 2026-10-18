namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Places one control point on each child of a group at a particle of this system, stepping
    /// through the particles from child to child, optionally turned along the particle's velocity
    /// or a vector attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetPerChildControlPoint">C_OP_SetPerChildControlPoint</seealso>
    class SetPerChildControlPoint : ParticleFunctionOperator
    {
        private readonly int childGroupId;
        private readonly int firstControlPoint;
        private readonly int numControlPoints = 1;
        private readonly INumberProvider particleIncrement = new LiteralNumberProvider(1f);
        private readonly INumberProvider firstSourcePoint = new LiteralNumberProvider(0f);
        private readonly bool setOrientation;
        private readonly ParticleField orientationField = ParticleField.NoneDisabled;
        private readonly bool numBasedOnParticleCount;

        public SetPerChildControlPoint(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            firstControlPoint = parse.Int32("m_nFirstControlPoint", firstControlPoint);
            numControlPoints = parse.Int32("m_nNumControlPoints", numControlPoints);
            particleIncrement = parse.CollectionNumberProvider("m_nParticleIncrement", particleIncrement);
            firstSourcePoint = parse.CollectionNumberProvider("m_nFirstSourcePoint", firstSourcePoint);
            setOrientation = parse.Boolean("m_bSetOrientation", setOrientation);
            orientationField = parse.ParticleField("m_nOrientationField", orientationField);
            numBasedOnParticleCount = parse.Boolean("m_bNumBasedOnParticleCount", numBasedOnParticleCount);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var count = numBasedOnParticleCount ? live.Length : numControlPoints;
            var source = (int)firstSourcePoint.NextNumber(particleSystemState);
            var increment = (int)particleIncrement.NextNumber(particleSystemState);
            var remaining = Math.Min(Math.Min(live.Length - source, particleSystemState.Children.Count), count);
            var step = increment % live.Length;

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

                ref var particle = ref live[source];
                var controlPoint = child.GetControlPoint(firstControlPoint);
                controlPoint.Position = particle.Position;

                if (setOrientation)
                {
                    var velocity = particle.Position - particle.PositionPrevious;

                    if (velocity != Vector3.Zero)
                    {
                        controlPoint.Orientation = velocity;
                    }
                }
                else if (orientationField != ParticleField.NoneDisabled)
                {
                    controlPoint.Orientation = MathUtils.SafeNormalize(particle.GetVector(orientationField), Vector3.UnitZ);
                }

                remaining--;
                source += step;

                if (source < 0)
                {
                    source += live.Length;
                }
            }
        }
    }
}
