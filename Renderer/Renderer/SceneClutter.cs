using System.Linq;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Instanced aggregate for the instances of a <see cref="WorldNode.ClutterSceneObject"/> that share a tint. A compute pass
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

        /// <summary>Gets the screen size fraction below which instances start to fade out.</summary>
        public float BeginCullSize { get; }

        /// <summary>Gets the screen size fraction below which instances are no longer drawn.</summary>
        public float EndCullSize { get; }

        internal int FirstCommand { get; set; }

        // One node spans many light probe volumes, so each fragment looks up the one it stands in
        private static readonly Dictionary<string, byte> ShaderArguments = new()
        {
            ["D_LPV_BLENDING"] = 1,
        };

        /// <summary>Initializes a clutter node.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model every instance draws.</param>
        /// <param name="materialGroup">Material group (skin) of the model, empty for the default.</param>
        /// <param name="beginCullSize">The clutter object's begin cull size.</param>
        /// <param name="endCullSize">The clutter object's end cull size.</param>
        /// <param name="instances">The instances to draw.</param>
        public SceneClutter(Scene scene, Model model, string materialGroup, float beginCullSize, float endCullSize, Instance[] instances)
            : base(scene, model, GetMaterialGroupTable(model, materialGroup), ShaderArguments)
        {
            ArgumentNullException.ThrowIfNull(instances);

            BeginCullSize = beginCullSize;
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

        private static Dictionary<string, string>? GetMaterialGroupTable(Model model, string materialGroup)
        {
            if (string.IsNullOrEmpty(materialGroup))
            {
                return null;
            }

            var materialGroups = model.GetMaterialGroups().ToArray();
            var group = materialGroups.FirstOrDefault(group => group.Name == materialGroup);

            if (group.Materials == null)
            {
                return null;
            }

            var table = new Dictionary<string, string>(group.Materials.Length);

            foreach (var (from, to) in materialGroups[0].Materials.Zip(group.Materials))
            {
                table[from] = to;
            }

            return table;
        }
    }
}
