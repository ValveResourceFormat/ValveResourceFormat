namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Connects each particle of the parent system to the nearest particle of this system through
    /// this system's children, one child per parent particle in order: the child's first control
    /// point goes to the parent particle and its second to the nearest particle, each facing along the
    /// connection. With <c>m_bUseRadius</c> the nearest particle is the one with the smallest gap
    /// between the two spheres, and the control points sit on the sphere surfaces, or both at the
    /// midpoint where the spheres touch. Among equally near particles, the one with the lowest index
    /// modulo eight wins, then the lowest index.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConnectParentParticleToNearest">C_OP_ConnectParentParticleToNearest</seealso>
    class ConnectParentParticleToNearest : ParticleFunctionOperator
    {
        private const int SearchLanes = 8;

        private readonly int firstControlPoint;
        private readonly int secondControlPoint = 1;
        private readonly bool useRadius;
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider parentRadiusScale = new LiteralNumberProvider(1f);

        public ConnectParentParticleToNearest(ParticleDefinitionParser parse) : base(parse)
        {
            firstControlPoint = parse.Int32("m_nFirstControlPoint", firstControlPoint);
            secondControlPoint = parse.Int32("m_nSecondControlPoint", secondControlPoint);
            useRadius = parse.Boolean("m_bUseRadius", useRadius);
            radiusScale = parse.CollectionNumberProvider("m_flRadiusScale", radiusScale);
            parentRadiusScale = parse.CollectionNumberProvider("m_flParentRadiusScale", parentRadiusScale);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;
            var children = particleSystemState.Children;
            var current = particles.Current;

            if (parentData == null || parentData.CurrentParticles.IsEmpty || children.Count == 0 || current.IsEmpty)
            {
                return;
            }

            var parentParticles = parentData.CurrentParticles;

            var scale = useRadius ? radiusScale.NextNumber(particleSystemState) : 0f;
            var parentScale = useRadius ? parentRadiusScale.NextNumber(particleSystemState) : 0f;
            var count = Math.Min(parentParticles.Length, children.Count);

            for (var i = 0; i < count; i++)
            {
                var parentPosition = parentParticles[i].Position;
                var parentRadius = parentParticles[i].Radius * parentScale;

                var nearest = -1;
                var best = float.MaxValue;

                for (var j = 0; j < current.Length; j++)
                {
                    var distance = useRadius
                        ? Vector3.Distance(current[j].Position, parentPosition) - parentRadius - (current[j].Radius * scale)
                        : Vector3.DistanceSquared(current[j].Position, parentPosition);

                    if (distance < best || (distance == best && j % SearchLanes < nearest % SearchLanes))
                    {
                        best = distance;
                        nearest = j;
                    }
                }

                if (nearest < 0)
                {
                    continue;
                }

                var particle = current[nearest];
                var first = parentPosition;
                var second = particle.Position;

                if (useRadius)
                {
                    if (best <= 0f)
                    {
                        first = second = (parentPosition + particle.Position) * 0.5f;
                    }
                    else
                    {
                        var direction = MathUtils.SafeNormalize(particle.Position - parentPosition);
                        first = parentPosition + (direction * parentRadius);
                        second = particle.Position - (direction * (particle.Radius * scale));
                    }
                }

                var child = children[i];
                var firstPoint = child.GetControlPoint(firstControlPoint);
                var secondPoint = child.GetControlPoint(secondControlPoint);

                firstPoint.Position = first;
                secondPoint.Position = second;

                if (second != first)
                {
                    firstPoint.Orientation = second - first;
                }

                if (second != particle.PositionPrevious)
                {
                    secondPoint.Orientation = second - particle.PositionPrevious;
                }
            }
        }
    }
}
