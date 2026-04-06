namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particle normals from a control point orientation with an added random offset.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalOffset">C_INIT_NormalOffset</seealso>
    class NormalOffset : ParticleFunctionInitializer
    {
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.Zero;
        private readonly int controlPointNumber;
        private readonly bool localCoords;
        private readonly bool normalize = true;

        public NormalOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.Vector3("m_OffsetMin", offsetMin);
            offsetMax = parse.Vector3("m_OffsetMax", offsetMax);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var offset = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, offsetMin, offsetMax);
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);
            var baseNormal = controlPoint.Orientation;

            Vector3 normal;
            if (localCoords && baseNormal != Vector3.Zero)
            {
                normal = TransformLocalOffset(offset, baseNormal);
                normal += baseNormal;
            }
            else if (baseNormal != Vector3.Zero)
            {
                normal = baseNormal + offset;
            }
            else
            {
                normal = offset;
            }

            if (normalize && normal != Vector3.Zero)
            {
                normal = Vector3.Normalize(normal);
            }

            particle.SetVector(ParticleField.Normal, normal);
            return particle;
        }

        private static Vector3 TransformLocalOffset(Vector3 offset, Vector3 forward)
        {
            var z = Vector3.Normalize(forward);
            var reference = Math.Abs(z.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            var x = Vector3.Normalize(Vector3.Cross(reference, z));
            var y = Vector3.Cross(z, x);

            return (offset.X * x) + (offset.Y * y) + (offset.Z * z);
        }
    }
}
