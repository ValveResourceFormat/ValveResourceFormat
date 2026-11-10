using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization for physics traces: draws the player collision hull while player
    /// movement is active, and sweeps a small box from the camera to show where it lands.
    /// </summary>
    public static class PhysicsTraceDebugRenderer
    {
        private const float CameraTraceRange = 10000f;
        private const float ContactBallRadius = 0.5f;
        private const float NormalLineLength = 16f;
        private const int ContactBallSegments = 16;
        private static readonly Vector3 CameraTraceHalfExtents = new(8f, 8f, 8f);
        private static readonly Color32 ContactColor = new(1f, 0.5f, 0f, 1f);
        private static readonly Color32 NormalColor = new(0.25f, 1f, 1f, 1f);

        /// <summary>Draws the debug geometry for the current frame as blended lines the scene can hide.</summary>
        /// <param name="context">Where the lines go.</param>
        /// <param name="physics">Physics world to trace against.</param>
        /// <param name="input">User input providing the player movement state.</param>
        /// <param name="camera">Camera the debug trace is fired from.</param>
        public static void Draw(ToolRenderContext context, PhysicsWorld physics, UserInput input, Camera camera)
        {
            context.PushRenderMode(ToolRenderMode.WireframeTranslucent);

            // Player collision hull while walk-mode movement is driving the camera
            if (input.WalkMode)
            {
                var movement = input.PlayerMovement;
                var halfExtents = movement.HullHalfExtents;
                var feet = movement.Position;
                var hullMins = feet - new Vector3(halfExtents.X, halfExtents.Y, 0f);
                var hullMaxs = feet + new Vector3(halfExtents.X, halfExtents.Y, halfExtents.Z * 2f);

                // Green when grounded, red in air
                context.DrawColor = movement.OnGround ? new Color32(0f, 1f, 0f, 1f) : Color32.Red;
                context.DrawWireBox(hullMins, hullMaxs);

                // Sweep the hull down a bit and mark the ground contact point
                var hullCenter = feet + new Vector3(0f, 0f, halfExtents.Z);
                var groundTrace = physics.TraceAABB(hullCenter, hullCenter - Vector3.UnitZ * 2f, halfExtents, "player", computeContactPoint: true);

                if (groundTrace.Hit)
                {
                    DrawContact(context, groundTrace.ContactPoint, groundTrace.HitNormal);
                }
            }

            // Sweep a 16x16x16 box from the camera and mark where it ends up
            var from = camera.Location;
            var to = from + camera.Forward * CameraTraceRange;
            var trace = physics.TraceAABB(from, to, CameraTraceHalfExtents, "player", computeContactPoint: true);

            var end = trace.Hit ? trace.HitPosition : to;
            var endColor = trace.Hit ? new Color32(1f, 1f, 1f, 1f) : new Color32(1f, 0.25f, 0.25f, 1f);

            context.DrawLine(from, end, new Color32(1f, 1f, 1f, 0.5f));

            context.DrawColor = endColor;
            context.DrawWireBox(end - CameraTraceHalfExtents, end + CameraTraceHalfExtents);

            if (trace.Hit)
            {
                DrawContact(context, trace.ContactPoint, trace.HitNormal);
            }

            context.PopRenderMode();
        }

        private static void DrawContact(ToolRenderContext context, Vector3 point, Vector3 normal)
        {
            context.DrawSphereCircles(point, ContactBallRadius, ContactBallSegments, ContactColor);
            context.DrawLine(point, point + normal * NormalLineLength, NormalColor);
        }
    }
}
