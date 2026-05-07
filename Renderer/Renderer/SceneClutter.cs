using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Scene node for instanced rendering of clutter objects.
    /// </summary>
    public class SceneClutter : SceneNode
    {
        /// <summary>
        /// Clutter tile.
        /// </summary>
        public struct ClutterTile
        {
            /// <summary>Gets or sets the index of the first instance in this tile. </summary>
            public int FirstInstance { get; set; }

            /// <summary>Gets or sets the index of the last instance in this tile. </summary>
            public int LastInstance { get; set; }

            /// <summary>Gets or sets the world-space bounding box for this tile. </summary>
            public AABB BoundsWs { get; set; }
        }

        /// <summary>Gets the model scene node used to render this clutter.</summary>
        public ModelSceneNode InstancedModel { get; }

        /// <summary>Gets the list of instance positions.</summary>
        public List<Vector3> InstancePositions { get; private set; } = [];

        /// <summary>Gets the list of instance orientations (packed 32-bit).</summary>
        public List<uint> InstanceOrientations { get; private set; } = [];

        /// <summary>Gets the list of instance scales.</summary>
        public List<float> InstanceScales { get; private set; } = [];

        /// <summary>Gets the list of instance tint colors (sRGB).</summary>
        public List<Color32> InstanceTints { get; private set; } = [];

        /// <summary>The tiles that make up this clutter.</summary>
        public List<ClutterTile> Tiles { get; private set; } = [];

        /// <summary>The screen size at which each clutter instance begins to fade out.</summary>
        public float BeginCullSize { get; set; } = 0.02f;

        /// <summary>The screen size at which each clutter instance is fully culled.</summary>
        public float EndCullSize { get; set; } = 0.0125f;

        /// <summary>Gets the bounding sphere radius of the instanced model.</summary>
        public float ModelRadius { get; }

        /// <summary>Initializes the scene clutter, loading the model and setting material group.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model resource providing the embedded or referenced mesh.</param>
        /// <param name="materialGroup">Material group name.</param>
        public SceneClutter(Scene scene, Model model, string? materialGroup)
            : base(scene)
        {
            InstancedModel = new ModelSceneNode(scene, model, materialGroup, isWorldPreview: true);
            LocalBoundingBox = InstancedModel.LocalBoundingBox;

            // Calculate bounding sphere radius from model's AABB
            var modelBounds = InstancedModel.LocalBoundingBox;
            var extents = modelBounds.Max - modelBounds.Min;
            ModelRadius = extents.Length() * 0.5f;
        }

        /// <summary>Parses instance data from the scene object.</summary>
        /// <param name="clutterSceneObject">KV3 object describing the clutter's instance data.</param>
        public void LoadInstanceData(KVObject clutterSceneObject)
        {
            // Load bounding box
            var bounds = clutterSceneObject.GetSubCollection("m_Bounds");
            var minBounds = bounds.GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = bounds.GetSubCollection("m_vMaxBounds").ToVector3();
            LocalBoundingBox = new AABB(minBounds, maxBounds);

            // Load instance positions
            var positions = clutterSceneObject.GetArray("m_instancePositions");
            InstancePositions = [.. positions.Select(p => p.ToVector3())];

            // Load instance orientations
            var orientations = clutterSceneObject.GetIntegerArray("m_InstanceOrientations32");
            InstanceOrientations = [.. orientations.Select(o => (uint)o)];

            // Load instance scales
            InstanceScales = [.. clutterSceneObject.GetFloatArray("m_instanceScales")];

            // Load instance tints
            var tints = clutterSceneObject.GetArray("m_instanceTintSrgb");
            InstanceTints = [.. tints.Select(t => t.ToVector3()).Select(v => new Color32(v[0], v[1], v[2], 255))];

            // Load tiles
            var tiles = clutterSceneObject.GetArray("m_tiles");
            Tiles = [.. tiles.Select(tile =>
            {
                var boundsWs = tile.GetSubCollection("m_BoundsWs");
                return new ClutterTile
                {
                    FirstInstance = tile.GetInt32Property("m_nFirstInstance"),
                    LastInstance = tile.GetInt32Property("m_nLastInstance"),
                    BoundsWs = new AABB(
                        boundsWs.GetSubCollection("m_vMinBounds").ToVector3(),
                        boundsWs.GetSubCollection("m_vMaxBounds").ToVector3()
                    )
                };
            })];

            // Load cull sizes
            BeginCullSize = clutterSceneObject.GetFloatProperty("m_flBeginCullSize", BeginCullSize);
            EndCullSize = clutterSceneObject.GetFloatProperty("m_flEndCullSize", EndCullSize);
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => InstancedModel.GetSupportedRenderModes();

#if DEBUG
        /// <inheritdoc/>
        public override void UpdateVertexArrayObjects() => InstancedModel.UpdateVertexArrayObjects();
#endif

        /// <inheritdoc/>
        public override void Delete()
        {
            InstancedModel.Delete();
        }
    }
}
