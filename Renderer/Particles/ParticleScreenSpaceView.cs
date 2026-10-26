namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// The fixed view screen-space particle systems are laid out in and drawn through. It looks from the
    /// world origin with world +X to the left of the screen and +Y up, across a box 200 units on each side
    /// that is fitted to cover a 16:9 frame, whatever the viewport.
    /// </summary>
    internal static class ParticleScreenSpaceView
    {
        private const float Extent = 200f;
        private const float Depth = 100f;
        private const float AuthoredAspectRatio = 16f / 9f;

        private static readonly Vector3 Angles = new(-90f, 0f, -90f);

        /// <summary>Points <paramref name="camera"/> at the screen-space view of a viewport.</summary>
        /// <param name="camera">The camera to set up. Its perspective projection is replaced.</param>
        /// <param name="windowSize">The viewport the view covers, in pixels.</param>
        public static void Configure(Camera camera, Vector2 windowSize)
        {
            var aspectRatio = windowSize.X / windowSize.Y;
            var width = Extent * MathF.Min(1f, aspectRatio / AuthoredAspectRatio);
            var height = Extent * MathF.Min(1f, AuthoredAspectRatio / aspectRatio);

            camera.Location = Vector3.Zero;
            camera.SetFromQAngle(Angles);
            camera.SetViewportSize((int)windowSize.X, (int)windowSize.Y);
            camera.SetOrthographicProjection(width, height, -Depth, Depth);
            camera.RecalculateMatrices();
        }

        /// <summary>
        /// How a screen-aligned card's extents scale along its right and up axes to fit the viewport.
        /// </summary>
        /// <param name="windowSize">The viewport the card is drawn into, in pixels.</param>
        public static Vector2 GetScreenAlignedExtentScale(Vector2 windowSize)
        {
            var aspectRatio = windowSize.X / windowSize.Y;

            if (aspectRatio > AuthoredAspectRatio)
            {
                return new Vector2(1f, AuthoredAspectRatio / aspectRatio);
            }

            return new Vector2(aspectRatio / AuthoredAspectRatio, 1f);
        }
    }
}
