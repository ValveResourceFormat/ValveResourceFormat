using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes a transform input's orientation into every particle's pitch/yaw/roll rotations (or its
    /// forward direction into the normal), plus a fixed rotation offset. The angle path adds the
    /// offset to the transform's pitch, yaw and roll; the quaternion path composes the offset in the
    /// transform's local frame. The value is uniform across all particles each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformOrientationToRotations">C_OP_RemapTransformOrientationToRotations</seealso>
    class RemapTransformOrientationToRotations : ParticleFunctionOperator
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

        /// <inheritdoc/>
        public override ulong StoredFields => writeNormal
            ? ParticleField.Normal.Mask()
            : ParticleField.Pitch.Mask() | ParticleField.Yaw.Mask() | ParticleField.Roll.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var (anglesRadians, forward) = TransformOrientationMath.Compute(transformInput, particleSystemState, rotationOffset, useQuat);

            var current = particles.Current;

            if (writeNormal)
            {
                current.Vectors(ParticleField.Normal).Fill(forward);
                return;
            }

            current.Floats(ParticleField.Pitch).Fill(anglesRadians.X);
            current.Floats(ParticleField.Yaw).Fill(anglesRadians.Y);
            current.Floats(ParticleField.Roll).Fill(anglesRadians.Z);
        }
    }
}
