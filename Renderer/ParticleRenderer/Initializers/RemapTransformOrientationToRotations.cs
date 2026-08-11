using ValveResourceFormat.Renderer.Particles.Operators;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Writes a transform input's orientation into a particle's pitch/yaw/roll rotations (or its
    /// forward direction into the normal) at spawn, plus a fixed rotation offset. Shares its math
    /// with the operator variant.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapTransformOrientationToRotations">C_INIT_RemapTransformOrientationToRotations</seealso>
    class RemapTransformOrientationToRotations : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly Vector3 rotationOffset = Vector3.Zero;
        private readonly bool useQuat;
        private readonly bool writeNormal;

        public RemapTransformOrientationToRotations(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            rotationOffset = parse.Vector3("m_vecRotation", rotationOffset);
            useQuat = parse.Boolean("m_bUseQuat", useQuat);
            writeNormal = parse.Boolean("m_bWriteNormal", writeNormal);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            TransformOrientationMath.Compute(transformInput, particleSystemState, rotationOffset, useQuat,
                out var anglesRadians, out var forward);

            if (writeNormal)
            {
                particle.Normal = forward;
            }
            else
            {
                particle.SetScalar(ParticleField.Pitch, anglesRadians.X);
                particle.SetScalar(ParticleField.Yaw, anglesRadians.Y);
                particle.SetScalar(ParticleField.Roll, anglesRadians.Z);
            }

            return particle;
        }
    }
}
