namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Writes a transform input's orientation into every particle's pitch/yaw/roll rotations (or its
    /// forward direction into the normal), plus a fixed rotation offset. The angle path derives
    /// pitch and yaw from the forward direction with roll always zero; the quaternion path composes
    /// the offset in the transform's local frame and preserves roll. The value is uniform across all
    /// particles each frame.
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            TransformOrientationMath.Compute(transformInput, particleSystemState, rotationOffset, useQuat,
                out var anglesRadians, out var forward);

            foreach (ref var particle in particles.Current)
            {
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
            }
        }
    }

    /// <summary>
    /// The engine's transform-orientation-to-angles math, shared by the operator and initializer
    /// variants. Angles follow the Source pitch/yaw/roll convention with X as forward.
    /// </summary>
    static class TransformOrientationMath
    {
        /// <param name="transformInput">The transform whose orientation is read.</param>
        /// <param name="state">The system state the transform resolves against.</param>
        /// <param name="rotationOffset">Fixed rotation offset in degrees; component order depends on <paramref name="useQuat"/>.</param>
        /// <param name="useQuat">Whether to compose via quaternions, preserving roll.</param>
        /// <param name="anglesRadians">Pitch, yaw, roll in radians.</param>
        /// <param name="forward">The rotated forward direction.</param>
        public static void Compute(ITransformProvider transformInput, ParticleSystemRenderState state,
            Vector3 rotationOffset, bool useQuat, out Vector3 anglesRadians, out Vector3 forward)
        {
            var transform = transformInput.NextTransform(state);
            var baseRotation = Quaternion.CreateFromRotationMatrix(transform);

            if (useQuat)
            {
                // The offset's components map pitch, yaw, roll = Y, Z, X on this path
                var offset = EntityTransformHelper.EulerAnglesToQuaternion(
                    new Vector3(rotationOffset.Y, rotationOffset.Z, rotationOffset.X));
                var final = offset * baseRotation;

                anglesRadians = Vector3.DegreesToRadians(EntityTransformHelper.ToEulerAngles(final));
                forward = Vector3.Transform(Vector3.UnitX, final);
            }
            else
            {
                var baseForward = Vector3.Transform(Vector3.UnitX, baseRotation);

                // The offset's components map pitch, yaw, roll = X, Y, Z on this path
                var angles = EntityTransformHelper.ForwardDirectionToEulerAngles(baseForward) + rotationOffset;

                anglesRadians = Vector3.DegreesToRadians(angles);
                forward = EntityTransformHelper.EulerAnglesToForwardDirection(angles);
            }
        }
    }
}
