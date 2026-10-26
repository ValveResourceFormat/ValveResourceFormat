using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Transform orientation to angles math, shared by the operator and initializer
    /// variants. Angles follow the Source pitch/yaw/roll convention with X as forward.
    /// </summary>
    static class TransformOrientationMath
    {
        /// <summary>
        /// Evaluates <paramref name="transformInput"/> and turns its orientation, with
        /// <paramref name="rotationOffset"/> applied, into angles and a forward direction.
        /// </summary>
        /// <param name="transformInput">The transform whose orientation is read.</param>
        /// <param name="state">The system state the transform resolves against.</param>
        /// <param name="rotationOffset">Fixed rotation offset in degrees; component order depends on <paramref name="useQuat"/>.</param>
        /// <param name="useQuat">Whether to compose via quaternions rather than adding angles.</param>
        /// <returns>Pitch, yaw and roll in radians, and the rotated forward direction.</returns>
        public static (Vector3 AnglesRadians, Vector3 Forward) Compute(ITransformProvider transformInput, ParticleSystemState state,
            Vector3 rotationOffset, bool useQuat)
        {
            var transform = transformInput.NextTransform(state);
            var baseRotation = Quaternion.CreateFromRotationMatrix(transform);

            if (useQuat)
            {
                // The offset's components map pitch, yaw, roll = Y, Z, X on this path
                var offset = EntityTransformHelper.EulerAnglesToQuaternion(
                    new Vector3(rotationOffset.Y, rotationOffset.Z, rotationOffset.X));
                var final = baseRotation * offset;

                return (Vector3.DegreesToRadians(EntityTransformHelper.ToEulerAngles(final)), Vector3.Transform(Vector3.UnitX, final));
            }

            // The offset's components map pitch, yaw, roll = X, Y, Z on this path
            var angles = EntityTransformHelper.ToEulerAngles(baseRotation) + rotationOffset;

            return (Vector3.DegreesToRadians(angles), EntityTransformHelper.EulerAnglesToForwardDirection(angles));
        }
    }
}
