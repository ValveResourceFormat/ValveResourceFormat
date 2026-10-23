namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps particles inside an axis-aligned box: in world space with a control point of -1, offset
    /// by the control point's position, or in the control point's frame with <c>m_bLocalSpace</c>.
    /// With <c>m_bAccountForRadius</c> the box shrinks by each particle's radius. Only
    /// <see cref="Particle.Position"/> is written, so the correction also changes the particle's
    /// velocity. In a box whose minimum lies above its maximum, the minimum wins with the radius and
    /// the maximum without it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_BoxConstraint">C_OP_BoxConstraint</seealso>
    class BoxConstraint : ParticleFunctionConstraint
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);
        private readonly int controlPoint;
        private readonly bool localSpace;
        private readonly bool accountForRadius;

        public BoxConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.CollectionVectorProvider("m_vecMin", min);
            max = parse.CollectionVectorProvider("m_vecMax", max);
            controlPoint = parse.Int32("m_nCP", controlPoint);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
            accountForRadius = parse.Boolean("m_bAccountForRadius", accountForRadius);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var boxMin = min.NextVector(particleSystemState);
            var boxMax = max.NextVector(particleSystemState);

            var toWorld = Matrix4x4.Identity;
            var toBox = Matrix4x4.Identity;

            if (controlPoint != -1)
            {
                var point = particleSystemState.GetControlPoint(controlPoint);

                if (localSpace)
                {
                    toWorld = point.GetTransform();
                    Matrix4x4.Invert(toWorld, out toBox);
                }
                else
                {
                    toWorld = Matrix4x4.CreateTranslation(point.Position);
                    toBox = Matrix4x4.CreateTranslation(-point.Position);
                }
            }

            var moved = false;

            foreach (var particle in particles.Current)
            {
                var local = Vector3.Transform(particle.Position, toBox);

                var clamped = accountForRadius
                    ? Vector3.Max(boxMin + new Vector3(particle.Radius), Vector3.Min(boxMax - new Vector3(particle.Radius), local))
                    : Vector3.Min(Vector3.Max(local, boxMin), boxMax);

                if (clamped == local)
                {
                    continue;
                }

                particle.Position = Vector3.Transform(clamped, toWorld);
                moved = true;
            }

            return moved;
        }
    }
}
