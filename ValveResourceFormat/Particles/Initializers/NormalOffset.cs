namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Adds a random offset to a particle's existing normal, optionally rotated into control point local space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalOffset">C_INIT_NormalOffset</seealso>
    class NormalOffset : ParticleFunctionInitializer
    {
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.Zero;
        private readonly int controlPointNumber;
        private readonly bool localCoords;
        private readonly bool normalize;

        public NormalOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.Vector3("m_OffsetMin", offsetMin);
            offsetMax = parse.Vector3("m_OffsetMax", offsetMax);
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        public override ulong WrittenFields => ParticleField.CreationTime.Mask() | ParticleField.Normal.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var normals = run.Vectors(ParticleField.Normal);
            var creationTimes = localCoords ? run.ReadFloats(ParticleField.CreationTime) : default;
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            for (var i = 0; i < normals.Length; i++)
            {
                var offset = particleSystemState.Random.NextBetweenPerComponentSeededTwice(offsetMin, offsetMax);

                if (localCoords)
                {
                    var basis = controlPoint.GetBasisAtTime(creationTimes[i]);
                    offset = Vector3.TransformNormal(offset, basis);
                }

                var normal = normals[i] + offset;

                if (normalize)
                {
                    normal = MathUtils.SafeNormalize(normal);
                }

                normals[i] = normal;
            }
        }
    }
}
