namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Provides a transformation matrix that may be derived from a control point or other source.
    /// </summary>
    interface ITransformProvider
    {
        /// <summary>
        /// Returns a transformation matrix from the provider.
        /// </summary>
        Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState);

        /// <summary>
        /// Returns a transformation matrix using system-level state only.
        /// </summary>
        Matrix4x4 NextTransform(ParticleSystemRenderState renderState)
            => NextTransform(ref Particle.Default, renderState);

        /// <summary>
        /// Gets just the position component of the transform.
        /// </summary>
        Vector3 GetPosition(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var transform = NextTransform(ref particle, renderState);
            return transform.Translation;
        }

        /// <summary>
        /// Gets the orientation, meaning the forward direction, that this provider carries.
        /// </summary>
        /// <remarks>
        /// Providers that only place things answer <see langword="false"/> rather than reporting the +X an
        /// identity rotation would give, so a caller does not overwrite a direction with a meaningless one.
        /// That is the default, so a provider has to opt in to having an orientation rather than remember
        /// to opt out of reporting a bogus one.
        /// </remarks>
        /// <returns>Whether the provider has an orientation to report.</returns>
        bool TryGetOrientation(ref Particle particle, ParticleSystemRenderState renderState, out Vector3 orientation)
        {
            orientation = Vector3.Zero;
            return false;
        }
    }

    /// <summary>
    /// Provides a transform derived from a control point's position and optional orientation.
    /// </summary>
    readonly struct ControlPointTransformProvider : ITransformProvider
    {
        private readonly int controlPoint;
        private readonly bool useOrientation;

        public ControlPointTransformProvider(int controlPoint, bool useOrientation)
        {
            this.controlPoint = controlPoint;
            this.useOrientation = useOrientation;
        }

        /// <summary>
        /// Control point 0 with its orientation, matching the defaults a transform input
        /// deserializes to when the effect leaves <c>m_TransformInput</c> unset.
        /// </summary>
        public ControlPointTransformProvider() : this(0, true)
        {
        }

        /// <summary>
        /// Whether this control point says anything about which way it faces, as opposed to only where it is.
        /// </summary>
        private bool HasOrientation(ControlPoint cp)
            => useOrientation && (cp.Rotation is not null || cp.Orientation != Vector3.Zero);

        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var cp = renderState.GetControlPoint(controlPoint);
            var position = cp.Position;

            if (!HasOrientation(cp))
            {
                return Matrix4x4.CreateTranslation(position);
            }

            // A full rotation (e.g. from a map entity's angles) can express frames a bare forward
            // vector cannot; prefer it when the control point carries one.
            var rotation = cp.Rotation is { } fullRotation
                ? Matrix4x4.CreateFromQuaternion(fullRotation)
                : EntityTransformHelper.ForwardDirectionToRotationMatrix(cp.Orientation);

            return rotation * Matrix4x4.CreateTranslation(position);
        }

        /// <inheritdoc/>
        public bool TryGetOrientation(ref Particle particle, ParticleSystemRenderState renderState, out Vector3 orientation)
        {
            var cp = renderState.GetControlPoint(controlPoint);

            if (!HasOrientation(cp))
            {
                orientation = Vector3.Zero;
                return false;
            }

            // Forward is the frame's first row either way, and for the direction-only case that is the
            // direction itself, so there is nothing to build here.
            orientation = cp.Rotation is { } fullRotation
                ? Vector3.Transform(Vector3.UnitX, fullRotation)
                : Vector3.Normalize(cp.Orientation);

            return true;
        }

        /// <summary>
        /// Transforms a local offset into control point <paramref name="cp"/>'s frame (rotation + translation).
        /// </summary>
        public static Vector3 TransformPosition(ParticleSystemRenderState state, int cp, Vector3 localOffset)
            => Vector3.Transform(localOffset, new ControlPointTransformProvider(cp, true).NextTransform(ref Particle.Default, state));

        /// <summary>
        /// Rotates a local direction into control point <paramref name="cp"/>'s frame (rotation only, no translation).
        /// </summary>
        public static Vector3 TransformDirection(ParticleSystemRenderState state, int cp, Vector3 localDir)
            => Vector3.TransformNormal(localDir, new ControlPointTransformProvider(cp, true).NextTransform(ref Particle.Default, state));

        /// <summary>
        /// Rotates a single Euler angle, given in radians as <paramref name="angleField"/>'s component, by control
        /// point <paramref name="cp"/>'s orientation, and returns that same component of the composed rotation.
        /// </summary>
        public static float TransformAngle(ParticleSystemRenderState state, int cp, ParticleField angleField, float radians)
        {
            var degrees = float.RadiansToDegrees(radians);
            var angles = angleField switch
            {
                ParticleField.Pitch => new Vector3(degrees, 0f, 0f),
                ParticleField.Yaw => new Vector3(0f, degrees, 0f),
                _ => new Vector3(0f, 0f, degrees),
            };

            var composed = EntityTransformHelper.EulerAnglesToRotationMatrix(angles)
                * new ControlPointTransformProvider(cp, true).NextTransform(ref Particle.Default, state);
            var result = EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(composed));

            return float.DegreesToRadians(angleField switch
            {
                ParticleField.Pitch => result.X,
                ParticleField.Yaw => result.Y,
                _ => result.Z,
            });
        }
    }

    /// <summary>
    /// A transform provider that always returns the identity matrix.
    /// </summary>
    readonly struct IdentityTransformProvider : ITransformProvider
    {
        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return Matrix4x4.Identity;
        }
    }
}
