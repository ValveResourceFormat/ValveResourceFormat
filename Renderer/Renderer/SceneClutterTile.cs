using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Instanced aggregate for one tile of a <see cref="ClutterSceneObject"/>, drawing fewer of its
    /// instances as the tile gets smaller on screen and none once it falls below the end cull size.
    /// </summary>
    public class SceneClutterTile : SceneAggregate
    {
        private readonly ClutterSceneObject Clutter;
        private int? DrawnInstances;

        /// <summary>Gets the bounding radius of the largest instance, which the screen size is measured with.</summary>
        public float InstanceRadius { get; }

        /// <summary>Gets the screen size the last update measured.</summary>
        public float ScreenSize { get; private set; }

        /// <inheritdoc/>
        public override int VisibleInstanceCount => DrawnInstances ?? InstanceTransforms.Count;

        /// <summary>Initializes a clutter tile.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model every instance draws.</param>
        /// <param name="clutter">The clutter object the tile belongs to.</param>
        /// <param name="maxInstanceScale">The largest instance scale of the clutter object.</param>
        public SceneClutterTile(Scene scene, Model model, ClutterSceneObject clutter, float maxInstanceScale)
            : base(scene, model, clutter.MaterialGroup)
        {
            Clutter = clutter;
            InstanceRadius = RenderMesh.BoundingBox.Size.Length() * 0.5f * maxInstanceScale;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The screen size is the projected diameter of <see cref="InstanceRadius"/> at the point of the tile
        /// nearest to the camera, as a fraction of the screen width, and 1 when the camera is within that radius.
        /// The drawn instances are the first ones of the tile.
        /// </remarks>
        public override void Update(Scene.UpdateContext context)
        {
            var camera = context.Camera;
            var cameraPosition = camera.Location;
            var nearest = Vector3.Clamp(cameraPosition, BoundingBox.Min, BoundingBox.Max);
            var distance = Vector3.Distance(nearest, cameraPosition);

            // M11 is 1 / tan(horizontal fov / 2)
            ScreenSize = distance < InstanceRadius
                ? 1f
                : Math.Clamp(InstanceRadius * camera.ProjectionMatrix.M11 / distance, 0f, 1f);

            var density = Clutter.GetDensity(ScreenSize);
            DrawnInstances = (int)MathF.Round(InstanceTransforms.Count * density, MidpointRounding.AwayFromZero);
        }
    }
}
