using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Instanced aggregate for one tile of a <see cref="ClutterSceneObject"/>, drawing fewer of its
    /// instances as the tile gets smaller on screen and none below the end cull size.
    /// </summary>
    public sealed class SceneClutterTile : SceneAggregate
    {
        private readonly float InstanceRadius;
        private readonly float FullDensitySize;
        private readonly float EndSize;
        private int? DrawnInstances;

        /// <inheritdoc/>
        public override int VisibleInstanceCount => DrawnInstances ?? InstanceTransforms.Count;

        /// <summary>Initializes a clutter tile.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model every instance draws.</param>
        /// <param name="materialGroup">Material group (skin) of the model, empty for the default.</param>
        /// <param name="beginCullSize">Screen size at and above which all instances are drawn.</param>
        /// <param name="endCullSize">Screen size below which no instances are drawn.</param>
        /// <param name="maxInstanceScale">The largest instance scale of the clutter object.</param>
        public SceneClutterTile(Scene scene, Model model, string materialGroup, float beginCullSize, float endCullSize, float maxInstanceScale)
            : base(scene, model, materialGroup)
        {
            InstanceRadius = RenderMesh.BoundingBox.Size.Length() * 0.5f * maxInstanceScale;
            EndSize = Math.Clamp(endCullSize, 0f, 1f);
            FullDensitySize = Math.Clamp(beginCullSize, EndSize, 1f);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The screen size is the projected diameter of the largest instance at the point of the tile nearest
        /// to the camera, as a fraction of the screen width. The tile draws the first instances of its range,
        /// as many as its density between the end and full density sizes allows.
        /// </remarks>
        public override void Update(Scene.UpdateContext context)
        {
            var camera = context.Camera;
            var nearest = Vector3.Clamp(camera.Location, BoundingBox.Min, BoundingBox.Max);
            var distance = Vector3.Distance(nearest, camera.Location);

            // M11 is 1 / tan(horizontal fov / 2)
            var screenSize = distance < InstanceRadius
                ? 1f
                : MathUtils.Saturate(InstanceRadius * camera.ProjectionMatrix.M11 / distance);

            var density = EndSize > 1.01f * screenSize
                ? 0f
                : MathUtils.RemapValClamped(screenSize, EndSize, FullDensitySize, 0f, 1f);

            DrawnInstances = (int)MathF.Round(InstanceTransforms.Count * density, MidpointRounding.AwayFromZero);
        }
    }
}
