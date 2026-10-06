using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// One entry of <see cref="WorldNode.ClutterSceneObjects"/>: a model drawn at many compiled instance
    /// placements, such as detail props.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/ClutterSceneObject_t">ClutterSceneObject_t</seealso>
    public class ClutterSceneObject
    {
        /// <summary>Gets the model every instance draws.</summary>
        public string RenderableModel { get; }

        /// <summary>Gets the material group (skin) of the model, empty for the default.</summary>
        public string MaterialGroup { get; }

        /// <summary>Gets the index into <see cref="WorldNode.LayerNames"/>.</summary>
        public int Layer { get; }

        /// <summary>Gets the object type flags shared by all instances.</summary>
        public ObjectTypeFlags Flags { get; }

        /// <summary>Gets the screen size fraction at and above which instances are drawn at full density.</summary>
        public float BeginCullSize { get; }

        /// <summary>Gets the screen size fraction below which instances are no longer drawn.</summary>
        public float EndCullSize { get; }

        /// <summary>Gets the world space position of each instance.</summary>
        public Vector3[] InstancePositions { get; }

        /// <summary>Gets the uniform scale of each instance.</summary>
        public float[] InstanceScales { get; }

        /// <summary>Gets the world space rotation of each instance.</summary>
        public Quaternion[] InstanceOrientations { get; }

        /// <summary>Gets the sRGB tint of each instance, in the 0-255 range.</summary>
        public Vector3[] InstanceTints { get; }

        /// <summary>Gets the tiles that split the instances into spatially grouped ranges.</summary>
        public Tile[] Tiles { get; }

        /// <summary>A contiguous range of instances with the world space bounds they cover.</summary>
        /// <param name="FirstInstance">Index of the first instance in the tile.</param>
        /// <param name="EndInstance">Index one past the last instance in the tile.</param>
        /// <param name="Bounds">World space bounds of the tile's instances.</param>
        public readonly record struct Tile(int FirstInstance, int EndInstance, AABB Bounds);

        /// <summary>
        /// Reads a clutter scene object from its keyvalues.
        /// </summary>
        /// <param name="data">An entry of <see cref="WorldNode.ClutterSceneObjects"/>.</param>
        public ClutterSceneObject(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            RenderableModel = data.GetStringProperty("m_renderableModel");
            MaterialGroup = data.GetStringProperty("m_materialGroup");
            Layer = data.GetInt32Property("m_nLayer");
            Flags = data.GetEnumValue<ObjectTypeFlags>("m_flags", normalize: true);
            BeginCullSize = data.GetFloatProperty("m_flBeginCullSize");
            EndCullSize = data.GetFloatProperty("m_flEndCullSize");

            InstancePositions = [.. data.GetArray("m_instancePositions").Select(static position => position.ToVector3())];
            InstanceScales = data.GetFloatArray("m_instanceScales");
            InstanceOrientations = [.. data.GetIntegerArray("m_InstanceOrientations32").Select(static packed => MathUtils.UnpackQuaternion32(unchecked((uint)packed)))];
            InstanceTints = [.. data.GetArray("m_instanceTintSrgb").Select(static tint => tint.ToVector3())];
            Tiles = [.. data.GetArray("m_tiles").Select(static tile =>
            {
                var bounds = tile.GetSubCollection("m_BoundsWs");

                return new Tile(
                    tile.GetInt32Property("m_nFirstInstance"),
                    tile.GetInt32Property("m_nLastInstance"),
                    new AABB(bounds.GetSubCollection("m_vMinBounds").ToVector3(), bounds.GetSubCollection("m_vMaxBounds").ToVector3()));
            })];
        }

        /// <summary>
        /// Gets the world transform of one instance: its uniform scale, then rotation, then position.
        /// </summary>
        /// <param name="index">The instance index.</param>
        public Matrix4x4 GetInstanceTransform(int index)
            => Matrix4x4.CreateScale(InstanceScales[index])
                * Matrix4x4.CreateFromQuaternion(InstanceOrientations[index])
                * Matrix4x4.CreateTranslation(InstancePositions[index]);
    }
}
