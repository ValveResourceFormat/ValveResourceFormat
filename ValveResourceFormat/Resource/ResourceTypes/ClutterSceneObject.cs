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
        public string? RenderableModel { get; }

        /// <summary>Gets the material group (skin) of the model, empty for the default.</summary>
        public string MaterialGroup { get; }

        /// <summary>Gets the index into <see cref="WorldNode.LayerNames"/>.</summary>
        public int Layer { get; }

        /// <summary>Gets the object type flags shared by all instances.</summary>
        public ObjectTypeFlags Flags { get; }

        /// <summary>Gets the world space bounds of all instances.</summary>
        public AABB Bounds { get; }

        /// <summary>Gets the screen size fraction above which instances are drawn at full density.</summary>
        public float BeginCullSize { get; }

        /// <summary>Gets the screen size fraction below which instances are no longer drawn.</summary>
        public float EndCullSize { get; }

        /// <summary>Gets the world space position of each instance.</summary>
        public Vector3[] InstancePositions { get; }

        /// <summary>Gets the uniform scale of each instance.</summary>
        public float[] InstanceScales { get; }

        /// <summary>Gets the world space rotation of each instance.</summary>
        public Quaternion[] InstanceOrientations { get; }

        /// <summary>Gets the sRGB tint of each instance.</summary>
        public Color32[] InstanceTints { get; }

        /// <summary>Gets the number of instances.</summary>
        public int InstanceCount => InstancePositions.Length;

        /// <summary>
        /// Reads a clutter scene object from its keyvalues.
        /// </summary>
        /// <param name="data">An entry of <see cref="WorldNode.ClutterSceneObjects"/>.</param>
        public ClutterSceneObject(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            RenderableModel = data.GetStringProperty("m_renderableModel");
            MaterialGroup = data.GetStringProperty("m_materialGroup", string.Empty);
            Layer = data.GetInt32Property("m_nLayer");
            Flags = data.ContainsKey("m_flags") ? data.GetEnumValue<ObjectTypeFlags>("m_flags", normalize: true) : ObjectTypeFlags.None;
            BeginCullSize = data.GetFloatProperty("m_flBeginCullSize");
            EndCullSize = data.GetFloatProperty("m_flEndCullSize");

            var bounds = data.GetSubCollection("m_Bounds");
            Bounds = new AABB(bounds.GetSubCollection("m_vMinBounds").ToVector3(), bounds.GetSubCollection("m_vMaxBounds").ToVector3());

            var positions = data.GetArray("m_instancePositions") ?? [];
            var count = positions.Count;

            InstancePositions = new Vector3[count];
            InstanceOrientations = new Quaternion[count];
            InstanceTints = new Color32[count];
            InstanceScales = new float[count];

            var scales = data.GetFloatArray("m_instanceScales");
            var orientations = data.GetIntegerArray("m_InstanceOrientations32");
            var tints = data.GetArray("m_instanceTintSrgb") ?? [];

            for (var i = 0; i < count; i++)
            {
                InstancePositions[i] = positions[i].ToVector3();
                InstanceScales[i] = i < scales.Length ? scales[i] : 1f;
                InstanceOrientations[i] = i < orientations.Length ? DecodeOrientation(unchecked((uint)orientations[i])) : Quaternion.Identity;

                if (i < tints.Count)
                {
                    var tint = tints[i].ToVector3();
                    InstanceTints[i] = new Color32((byte)tint.X, (byte)tint.Y, (byte)tint.Z);
                }
                else
                {
                    InstanceTints[i] = Color32.White;
                }
            }
        }

        /// <summary>
        /// Gets the world transform of one instance: its uniform scale, then rotation, then position.
        /// </summary>
        /// <param name="index">The instance index.</param>
        public Matrix4x4 GetInstanceTransform(int index)
            => Matrix4x4.CreateScale(InstanceScales[index])
                * Matrix4x4.CreateFromQuaternion(InstanceOrientations[index])
                * Matrix4x4.CreateTranslation(InstancePositions[index]);

        /// <summary>
        /// Unpacks a rotation stored in 32 bits: x in the low 11 bits, y and z in the next 10 bits each,
        /// all mapped from [0, 1] to [-1, 1], with w rebuilt from unit length and negative when the top bit is set.
        /// </summary>
        /// <param name="packed">The packed rotation.</param>
        /// <returns>The normalized rotation.</returns>
        public static Quaternion DecodeOrientation(uint packed)
        {
            var x = (packed & 0x7FF) / 2047f * 2f - 1f;
            var y = ((packed >> 11) & 0x3FF) / 1023f * 2f - 1f;
            var z = ((packed >> 21) & 0x3FF) / 1023f * 2f - 1f;
            var w = MathF.Sqrt(MathF.Max(0f, 1f - x * x - y * y - z * z));

            if ((packed & 0x80000000) != 0)
            {
                w = -w;
            }

            return Quaternion.Normalize(new Quaternion(x, y, z, w));
        }
    }
}
