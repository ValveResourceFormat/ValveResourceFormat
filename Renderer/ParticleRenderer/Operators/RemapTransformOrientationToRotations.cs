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
                var offset = AngleQuaternion(rotationOffset.Y, rotationOffset.Z, rotationOffset.X);
                var final = offset * baseRotation;

                var angles = QuaternionAngles(final);
                anglesRadians = new Vector3(
                    float.DegreesToRadians(angles.X),
                    float.DegreesToRadians(angles.Y),
                    float.DegreesToRadians(angles.Z));
                forward = Vector3.Transform(Vector3.UnitX, final);
            }
            else
            {
                var baseForward = Vector3.Transform(Vector3.UnitX, baseRotation);

                // The offset's components map pitch, yaw, roll = X, Y, Z on this path
                var angles = VectorAngles(baseForward) + rotationOffset;
                anglesRadians = new Vector3(
                    float.DegreesToRadians(angles.X),
                    float.DegreesToRadians(angles.Y),
                    float.DegreesToRadians(angles.Z));
                forward = AngleVectors(angles);
            }
        }

        /// <summary>Pitch/yaw from a forward direction, roll zero, in degrees.</summary>
        private static Vector3 VectorAngles(Vector3 forward)
        {
            var lengthXY = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);

            if (lengthXY <= 0.001f)
            {
                return new Vector3(forward.Z > 0f ? -90f : 90f, 0f, 0f);
            }

            return new Vector3(
                float.RadiansToDegrees(MathF.Atan2(-forward.Z, lengthXY)),
                float.RadiansToDegrees(MathF.Atan2(forward.Y, forward.X)),
                0f);
        }

        /// <summary>Forward direction from pitch/yaw angles in degrees.</summary>
        private static Vector3 AngleVectors(Vector3 anglesDegrees)
        {
            var pitch = float.DegreesToRadians(anglesDegrees.X);
            var yaw = float.DegreesToRadians(anglesDegrees.Y);

            return new Vector3(
                MathF.Cos(yaw) * MathF.Cos(pitch),
                MathF.Sin(yaw) * MathF.Cos(pitch),
                -MathF.Sin(pitch));
        }

        /// <summary>Quaternion from pitch/yaw/roll degrees in the Source convention.</summary>
        private static Quaternion AngleQuaternion(float pitchDegrees, float yawDegrees, float rollDegrees)
        {
            var (sy, cy) = MathF.SinCos(float.DegreesToRadians(yawDegrees) * 0.5f);
            var (sp, cp) = MathF.SinCos(float.DegreesToRadians(pitchDegrees) * 0.5f);
            var (sr, cr) = MathF.SinCos(float.DegreesToRadians(rollDegrees) * 0.5f);

            return new Quaternion(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy);
        }

        /// <summary>Pitch/yaw/roll degrees from a quaternion in the Source convention.</summary>
        private static Vector3 QuaternionAngles(Quaternion rotation)
        {
            var forward = Vector3.Transform(Vector3.UnitX, rotation);
            var left = Vector3.Transform(Vector3.UnitY, rotation);
            var up = Vector3.Transform(Vector3.UnitZ, rotation);

            var lengthXY = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);

            if (lengthXY > 0.001f)
            {
                return new Vector3(
                    float.RadiansToDegrees(MathF.Atan2(-forward.Z, lengthXY)),
                    float.RadiansToDegrees(MathF.Atan2(forward.Y, forward.X)),
                    float.RadiansToDegrees(MathF.Atan2(left.Z, up.Z)));
            }

            return new Vector3(
                float.RadiansToDegrees(MathF.Atan2(-forward.Z, lengthXY)),
                float.RadiansToDegrees(MathF.Atan2(-left.X, left.Y)),
                0f);
        }
    }
}
