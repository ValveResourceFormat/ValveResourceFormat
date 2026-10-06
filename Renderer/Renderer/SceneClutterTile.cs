using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Instanced aggregate for one tile of a <see cref="ClutterSceneObject"/>, drawing fewer of its
    /// instances as the tile gets smaller on screen.
    /// </summary>
    public sealed class SceneClutterTile : SceneAggregate
    {
        private readonly float InstanceRadius;
        private readonly float[] InstanceThresholds;
        private int? DrawnInstances;

        /// <inheritdoc/>
        public override int VisibleInstanceCount => DrawnInstances ?? InstanceTransforms.Count;

        /// <summary>Initializes a clutter tile.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model every instance draws.</param>
        /// <param name="materialGroup">Material group (skin) of the model, empty for the default.</param>
        /// <param name="maxInstanceScale">The largest instance scale of the clutter object.</param>
        /// <param name="instances">World transform of each instance, with the screen size it needs to be drawn,
        /// from <see cref="GetDensityThreshold"/>.</param>
        public SceneClutterTile(Scene scene, Model model, string materialGroup, float maxInstanceScale, (Matrix4x4 Transform, float Threshold)[] instances)
            : base(scene, model, materialGroup)
        {
            ArgumentNullException.ThrowIfNull(instances);

            InstanceRadius = RenderMesh.BoundingBox.Size.Length() * 0.5f * maxInstanceScale;

            Array.Sort(instances, static (a, b) => a.Threshold.CompareTo(b.Threshold));
            InstanceThresholds = Array.ConvertAll(instances, static instance => instance.Threshold);
            InstanceTransforms.AddRange(Array.ConvertAll(instances, static instance => instance.Transform.To3x4()));
        }

        /// <summary>
        /// Gets the screen size one instance of a tile needs to be drawn. Thresholds spread from the end cull
        /// size up to half again as much, rising with the instance's index in the tile and a hash of it.
        /// </summary>
        /// <param name="indexInTile">Index of the instance within its tile.</param>
        /// <param name="tileInstanceCount">Number of instances in the tile.</param>
        /// <param name="endCullSize">The clutter object's end cull size.</param>
        public static float GetDensityThreshold(int indexInTile, int tileInstanceCount, float endCullSize)
        {
            var hash = MathUtils.HashUInt32((uint)indexInTile) * (1f / 4294967296f);

            return Math.Clamp(endCullSize, 0f, 1f) + 0.5f * endCullSize * hash * indexInTile / tileInstanceCount;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The screen size is the projected diameter of the largest instance at the point of the tile nearest
        /// to the camera, as a fraction of the screen width. Instances whose threshold it reaches are drawn.
        /// </remarks>
        public override void Update(Scene.UpdateContext context)
        {
            var camera = context.Camera;
            var nearest = Vector3.Clamp(camera.Location, BoundingBox.Min, BoundingBox.Max);
            var distance = Vector3.Distance(nearest, camera.Location);

            // M11 is 1 / tan(horizontal fov / 2)
            var screenSize = distance <= InstanceRadius
                ? 1f
                : MathUtils.Saturate(InstanceRadius * camera.ProjectionMatrix.M11 / distance);

            var low = 0;
            var high = InstanceThresholds.Length;

            while (low < high)
            {
                var middle = (low + high) / 2;

                if (InstanceThresholds[middle] <= screenSize)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            DrawnInstances = low;
        }
    }
}
