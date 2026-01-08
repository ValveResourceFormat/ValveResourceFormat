namespace GUI.Types.ParticleRenderer
{
    interface ITransformProvider
    {
        /// <summary>
        /// Returns a transformation matrix from the provider.
        /// </summary>
        Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState);

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

    readonly struct ControlPointTransformProvider : ITransformProvider
    {
        private readonly int controlPoint;
        private readonly bool useOrientation;

        public ControlPointTransformProvider(int controlPoint, bool useOrientation)
        {
            this.controlPoint = controlPoint;
            this.useOrientation = useOrientation;
        }

        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState)
        {
            var cp = renderState.GetControlPoint(controlPoint);
            var position = cp.Position;

            if (!useOrientation || cp.Orientation == Vector3.Zero)
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
    }

    readonly struct IdentityTransformProvider : ITransformProvider
    {
        public Matrix4x4 NextTransform(ref Particle particle, ParticleSystemRenderState renderState)
        {
            return Matrix4x4.Identity;
        }
    }
}
