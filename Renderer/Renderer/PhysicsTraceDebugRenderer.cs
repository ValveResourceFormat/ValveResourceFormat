using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization for physics traces: draws the player collision hull while player
    /// movement is active, and sweeps a small box from the camera to show where it lands.
    /// </summary>
    public class PhysicsTraceDebugRenderer : LineDebugRenderer
    {
        private const float CameraTraceRange = 10000f;
        private const float ContactBallRadius = 0.5f;
        private static readonly Vector3 CameraTraceHalfExtents = new(8f, 8f, 8f);
        private static readonly AABB CameraTraceBox = new(-CameraTraceHalfExtents, CameraTraceHalfExtents);
        private const float NormalLineLength = 16f;
        private static readonly Color32 ContactColor = new(1f, 0.5f, 0f, 1f);
        private static readonly Color32 NormalColor = new(0.25f, 1f, 1f, 1f);

        private readonly List<SimpleVertex> vertices = new(64);

        /// <summary>Initializes the renderer and creates GPU resources.</summary>
        /// <param name="rendererContext">Renderer context for loading shaders.</param>
        public PhysicsTraceDebugRenderer(RendererContext rendererContext)
            : base(rendererContext, nameof(PhysicsTraceDebugRenderer))
        {
        }

        /// <summary>Rebuilds and renders the debug geometry for the current frame.</summary>
        /// <param name="physics">Physics world to trace against.</param>
        /// <param name="input">User input providing the player movement state.</param>
        /// <param name="camera">Camera the debug trace is fired from.</param>
        public void Render(Rubikon physics, UserInput input, Camera camera)
        {
            Rebuild(physics, input, camera);
            RenderLines();
        }

        private void Rebuild(Rubikon physics, UserInput input, Camera camera)
        {
            vertices.Clear();

            // Player collision hull while walk-mode movement is driving the camera
            if (!input.NoClip)
            {
                var movement = input.PlayerMovement;
                var hull = movement.Hull.Translate(movement.Position);

                // Green when grounded, red in air
                var hullColor = movement.OnGround ? new Color32(0f, 1f, 0f, 1f) : Color32.Red;
                ShapeSceneNode.AddBox(vertices, hull, hullColor);

                // Sweep the hull down a bit and mark the ground contact point
                var hullCenter = movement.Position + new Vector3(0f, 0f, movement.Hull.Size.Z * 0.5f);
                var groundTrace = physics.TraceAABB(hullCenter, hullCenter - Vector3.UnitZ * 2f, movement.Hull, "player", computeContactPoint: true);

                if (groundTrace.Hit)
                {
                    ShapeSceneNode.AddSphere(vertices, groundTrace.ContactPoint, ContactBallRadius, ContactColor);
                    ShapeSceneNode.AddLine(vertices, groundTrace.ContactPoint, groundTrace.ContactPoint + groundTrace.HitNormal * NormalLineLength, NormalColor);
                }
            }

            // Sweep a 16x16x16 box from the camera and mark where it ends up
            var from = camera.Location;
            var to = from + camera.Forward * CameraTraceRange;
            var trace = physics.TraceAABB(from, to, CameraTraceBox, "player", computeContactPoint: true);

            var end = trace.Hit ? trace.HitPosition : to;
            var endColor = trace.Hit ? new Color32(1f, 1f, 1f, 1f) : new Color32(1f, 0.25f, 0.25f, 1f);

            ShapeSceneNode.AddLine(vertices, from, end, new Color32(1f, 1f, 1f, 0.5f));
            ShapeSceneNode.AddBox(vertices, new AABB(end - CameraTraceHalfExtents, end + CameraTraceHalfExtents), endColor);

            if (trace.Hit)
            {
                ShapeSceneNode.AddSphere(vertices, trace.ContactPoint, ContactBallRadius, ContactColor);
                ShapeSceneNode.AddLine(vertices, trace.ContactPoint, trace.ContactPoint + trace.HitNormal * NormalLineLength, NormalColor);
            }

            Upload(vertices);
        }
    }
}
