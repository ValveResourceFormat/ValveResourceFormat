namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// Picks where to put the camera when the viewer focuses something.
    /// </summary>
    public static class CameraPlacement
    {
        /// <summary> Hull probed with first: a standing human.</summary>
        public static readonly Vector3 PlayerHalfExtents = new(8f, 8f, 36f);

        /// <summary>Hull probed with second: a small cube representing the camera.</summary>
        public static readonly Vector3 SmallHalfExtents = new(8f, 8f, 8f);

        /// <summary>Upper bound on the number of probed positions, so focusing stays a cheap one-shot action.</summary>
        private const int MaxTests = 24;

        /// <summary>Gap left between the camera and the surface it was pulled in against.</summary>
        private const float SurfaceMargin = 2f;

        /// <summary>
        /// Yaws the camera orbits from, in radians. Maps are built on the grid, so the four axis
        /// directions (+X, -X, +Y, -Y) look square-on to most surfaces and are tried before the
        /// diagonals in between.
        /// </summary>
        private static readonly float[] Yaws =
        [
            0f, MathF.PI, MathF.PI / 2f, -MathF.PI / 2f,
            MathF.PI / 4f, -MathF.PI / 4f, 3f * MathF.PI / 4f, -3f * MathF.PI / 4f,
        ];

        /// <summary>Elevations tried at each yaw: the usual one, then flat for low rooms, then from above.</summary>
        private static readonly float[] Elevations = [30f * MathF.PI / 180f, 15f * MathF.PI / 180f, 60f * MathF.PI / 180f];

        /// <summary>
        /// Finds a camera position orbiting <paramref name="center"/> that is not embedded in world
        /// geometry and has a clear line to the target.
        /// </summary>
        /// <param name="physics">Physics world to probe against; when <see langword="null"/> the preferred position is returned as-is.</param>
        /// <param name="center">World-space point the camera should look at.</param>
        /// <param name="distance">Orbit radius that frames the target.</param>
        /// <param name="targetRadius">Radius of the focused object, so probes start outside its own collision.</param>
        /// <returns>The chosen camera position, falling back to the preferred one when the target cannot be seen from anywhere.</returns>
        public static Vector3 FindOrbitPosition(Rubikon? physics, Vector3 center, float distance, float targetRadius = 0f)
        {
            var preferred = OrbitPosition(center, distance, Yaws[0], Elevations[0]);

            if (physics == null)
            {
                return preferred;
            }

            if (TryFindOrbitPosition(physics, center, distance, targetRadius, PlayerHalfExtents, preferred, out var playerFit))
            {
                return playerFit;
            }

            // Tight interiors and clutter can leave the player hull nowhere to go, so a second pass
            // shrinks the camera to a box that only has to avoid clipping through a surface.
            TryFindOrbitPosition(physics, center, distance, targetRadius, SmallHalfExtents, playerFit, out var smallFit);

            return smallFit;
        }

        /// <summary>
        /// Probes the orbit candidates with one hull size. Returns <see langword="false"/> when no
        /// candidate could see the target, in which case <paramref name="position"/> holds the best
        /// non-solid spot found, or <paramref name="fallback"/> when there was not even one of those.
        /// </summary>
        private static bool TryFindOrbitPosition(Rubikon physics, Vector3 center, float distance, float targetRadius,
            Vector3 halfExtents, Vector3 fallback, out Vector3 position)
        {
            var padding = MathF.Max(halfExtents.X, halfExtents.Z);

            // Probing from the middle of the focused object would just hit the object itself, so the
            // line of sight starts clear of its own bounds - but never past half the orbit radius.
            var eyeOffset = MathF.Min(targetRadius + padding + 1f, distance * 0.5f);
            var minimumReach = eyeOffset + padding;

            var preferred = fallback;
            var bestReach = 0f;
            var bestPosition = preferred;
            var firstFree = preferred;
            var foundFree = false;

            // Yaw is the inner axis so every horizontal angle is exhausted before the camera is
            // tilted: swinging around the target reads better than climbing over it.
            var candidateCount = Math.Min(Elevations.Length * Yaws.Length, MaxTests);

            for (var i = 0; i < candidateCount; i++)
            {
                var elevation = Elevations[i / Yaws.Length];
                var yaw = Yaws[i % Yaws.Length];

                var direction = OrbitDirection(yaw, elevation);
                var candidate = center + direction * distance;
                var eye = center + direction * eyeOffset;

                var trace = physics.TraceAABB(eye, candidate, halfExtents, Rubikon.DefaultGeometry, detectStartSolid: true);

                if (trace.StartSolid)
                {
                    // The target is buried on this side, so nothing along this direction is visible.
                    continue;
                }

                if (!trace.Hit)
                {
                    // Nothing in the way: the sweep having reached the end also proves the spot is free.
                    position = candidate;
                    return true;
                }

                // Blocked, so back the camera off to just short of what blocked it. That point is
                // on the cleared part of the sweep, so it is both free and looking at the target.
                var reach = eyeOffset + MathF.Max(trace.Distance - SurfaceMargin, 0f);

                if (reach > bestReach && reach >= minimumReach)
                {
                    bestReach = reach;
                    bestPosition = center + direction * reach;
                }

                // Last resort for a target that cannot be seen from any angle: a spot that is at
                // least not inside the map.
                if (!foundFree && !physics.CheckOverlap(candidate, halfExtents, Rubikon.DefaultGeometry))
                {
                    firstFree = candidate;
                    foundFree = true;
                }
            }

            if (bestReach > 0f)
            {
                position = bestPosition;
                return true;
            }

            position = foundFree ? firstFree : preferred;
            return false;
        }

        private static Vector3 OrbitDirection(float yaw, float elevation)
        {
            var cosElevation = MathF.Cos(elevation);

            return new Vector3(cosElevation * MathF.Cos(yaw), cosElevation * MathF.Sin(yaw), MathF.Sin(elevation));
        }

        private static Vector3 OrbitPosition(Vector3 center, float distance, float yaw, float elevation)
            => center + OrbitDirection(yaw, elevation) * distance;
    }
}
