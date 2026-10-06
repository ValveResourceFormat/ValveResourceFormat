using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Instanced aggregate for the instances of a <see cref="ClutterSceneObject"/> that share a tint. A compute pass
    /// picks the instances to draw each frame by their screen size.
    /// </summary>
    public sealed class SceneClutter : SceneAggregate
    {
        /// <summary>One instance with its place in the compiled tile it belongs to.</summary>
        /// <param name="Transform">World transform of the instance.</param>
        /// <param name="Scale">Uniform scale of the instance.</param>
        /// <param name="IndexInTile">Index of the instance within its tile.</param>
        /// <param name="TileInstanceCount">Number of instances in the tile.</param>
        public readonly record struct Instance(Matrix4x4 Transform, float Scale, int IndexInTile, int TileInstanceCount);

        internal ClutterCuller.InstanceGpu[] CullInstances { get; }

        internal float EndCullSize { get; }

        internal int FirstCommand { get; set; }

        /// <summary>Initializes a clutter node.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model every instance draws.</param>
        /// <param name="materialGroup">Material group (skin) of the model, empty for the default.</param>
        /// <param name="endCullSize">The clutter object's end cull size.</param>
        /// <param name="instances">The instances to draw.</param>
        public SceneClutter(Scene scene, Model model, string materialGroup, float endCullSize, Instance[] instances)
            : base(scene, model, materialGroup)
        {
            ArgumentNullException.ThrowIfNull(instances);

            EndCullSize = endCullSize;

            var meshBounds = RenderMesh.BoundingBox;
            var modelRadius = meshBounds.Size.Length() * 0.5f;
            var bounds = meshBounds.Transform(instances[0].Transform);

            CullInstances = new ClutterCuller.InstanceGpu[instances.Length];
            InstanceTransforms.EnsureCapacity(instances.Length);

            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                var transform = instance.Transform.To3x4();

                CullInstances[i] = new ClutterCuller.InstanceGpu
                {
                    Transform = transform,
                    Center = Vector3.Transform(meshBounds.Center, instance.Transform),
                    Radius = instance.Scale * modelRadius,
                    IndexInTile = (uint)instance.IndexInTile,
                    TileInstanceCount = (uint)instance.TileInstanceCount,
                };

                InstanceTransforms.Add(transform);
                bounds = bounds.Union(meshBounds.Transform(instance.Transform));
            }

            LocalBoundingBox = bounds;
        }
    }
}
