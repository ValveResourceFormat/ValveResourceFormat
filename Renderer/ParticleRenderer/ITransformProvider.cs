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
        /// Gets the orientation (forward direction) from the transform.
        /// </summary>
        Vector3 GetOrientation(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var transform = NextTransform(ref particle, renderState);
            return new Vector3(transform.M31, transform.M32, transform.M33);
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

        /// <inheritdoc/>
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var cp = renderState.GetControlPoint(controlPoint);
            var position = cp.Position;

            if (!useOrientation)
            {
                return Matrix4x4.CreateTranslation(position);
            }

            // A full rotation (e.g. from a map entity's angles) can express frames a bare forward
            // vector cannot; prefer it when the control point carries one.
            if (cp.Rotation is { } fullRotation)
            {
                return Matrix4x4.CreateFromQuaternion(fullRotation) * Matrix4x4.CreateTranslation(position);
            }

            if (cp.Orientation == Vector3.Zero)
            {
                return Matrix4x4.CreateTranslation(position);
            }

            var forward = Vector3.Normalize(cp.Orientation);
            var up = Math.Abs(forward.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(up, forward));
            up = Vector3.Cross(forward, right);

            var rotation = new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                0, 0, 0, 1
            );

            return rotation * Matrix4x4.CreateTranslation(position);
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
