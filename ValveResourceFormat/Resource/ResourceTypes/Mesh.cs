using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a mesh resource containing geometry and vertex buffer data.
    /// </summary>
    public class Mesh : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets or sets the mesh's vertex/index buffer block (VBIB).
        /// </summary>
        public VBIB VBIB
        {
            get
            {
                //new format has VBIB block, for old format we can get it from NTRO DATA block
                cachedVBIB ??= (VBIB)Resource.GetBlockByType(BlockType.VBIB) ?? new VBIB(Resource, Data);
                return cachedVBIB;
            }
            set
            {
                cachedVBIB = value;
            }
        }

        /// <summary>
        /// Gets or sets the mesh name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the minimum bounds of the mesh.
        /// </summary>
        public Vector3 MinBounds { get; private set; }

        /// <summary>
        /// Gets the maximum bounds of the mesh.
        /// </summary>
        public Vector3 MaxBounds { get; private set; }

        /// <summary>
        /// Gets or sets the morph data for this mesh.
        /// </summary>
        public Morph MorphData { get; set; }

        private VBIB cachedVBIB { get; set; }

        /// <summary>
        /// Gets the attachments associated with this mesh.
        /// </summary>
        public Dictionary<string, Attachment> Attachments { get; init; } = [];

        /// <summary>
        /// Gets the hitbox sets associated with this mesh.
        /// </summary>
        public Dictionary<string, Hitbox[]> HitboxSets { get; init; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="Mesh"/> class.
        /// </summary>
        /// <param name="type">The block type.</param>
        public Mesh(BlockType type) : base(type, "PermRenderMeshData_t")
        {
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            Name = Resource.FileName;

            if (Data.ContainsKey("m_attachments"))
            {
                var attachmentsData = Data.GetArray("m_attachments");
                for (var i = 0; i < attachmentsData.Length; i++)
                {
                    var attachment = new Attachment(attachmentsData[i]);
                    Attachments.Add(attachment.Name, attachment);
                }
            }
            if (Data.ContainsKey("m_hitboxsets"))
            {
                var hitboxSetsData = Data.GetArray("m_hitboxsets");
                for (var i = 0; i < hitboxSetsData.Length; i++)
                {
                    var hitboxSet = hitboxSetsData[i].GetSubCollection("value") ?? hitboxSetsData[i];
                    var hitboxSetName = hitboxSet.GetStringProperty("m_name");

                    var hitboxesKey = hitboxSet.ContainsKey("m_HitBoxes") ? "m_HitBoxes" : "m_hitboxes";
                    var hitboxes = hitboxSet.GetArray(hitboxesKey, d => new Hitbox(d));

                    HitboxSets.Add(hitboxSetName, hitboxes);
                }
            }
        }

        /// <summary>
        /// Calculates and sets the bounding box from scene objects.
        /// </summary>
        public void GetBounds()
        {
            var sceneObjects = Data.GetArray("m_sceneObjects");
            if (sceneObjects.Length == 0)
            {
                MinBounds = MaxBounds = new Vector3(0, 0, 0);
                return;
            }

            var minBounds = sceneObjects[0].GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = sceneObjects[0].GetSubCollection("m_vMaxBounds").ToVector3();

            for (var i = 1; i < sceneObjects.Length; ++i)
            {
                var localMin = sceneObjects[i].GetSubCollection("m_vMinBounds").ToVector3();
                var localMax = sceneObjects[i].GetSubCollection("m_vMaxBounds").ToVector3();

                minBounds.X = Math.Min(minBounds.X, localMin.X);
                minBounds.Y = Math.Min(minBounds.Y, localMin.Y);
                minBounds.Z = Math.Min(minBounds.Z, localMin.Z);
                maxBounds.X = Math.Max(maxBounds.X, localMax.X);
                maxBounds.Y = Math.Max(maxBounds.Y, localMax.Y);
                maxBounds.Z = Math.Max(maxBounds.Z, localMax.Z);
            }

            MinBounds = minBounds;
            MaxBounds = maxBounds;
        }

        /// <summary>
        /// Determines if compressed normal tangent is enabled for the draw call.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if compressed normal tangent is used.</returns>
        public static bool IsCompressedNormalTangent(KVObject drawCall)
        {
            if (drawCall.ContainsKey("m_bUseCompressedNormalTangent"))
            {
                return drawCall.GetProperty<bool>("m_bUseCompressedNormalTangent");
            }

            if (!drawCall.ContainsKey("m_nFlags"))
            {
                return false;
            }

            var flags = drawCall.GetProperty<object>("m_nFlags");

            return flags switch
            {
                string flagsString => flagsString.Contains("MESH_DRAW_FLAGS_USE_COMPRESSED_NORMAL_TANGENT", StringComparison.InvariantCulture),
                long flagsLong => ((RenderMeshDrawPrimitiveFlags)flagsLong & RenderMeshDrawPrimitiveFlags.UseCompressedNormalTangent) != 0,
                byte flagsByte => ((RenderMeshDrawPrimitiveFlags)flagsByte & RenderMeshDrawPrimitiveFlags.UseCompressedNormalTangent) != 0,
                _ => false
            };
        }

        /// <summary>
        /// Determines if the draw call has baked lighting from lightmap.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if baked lighting from lightmap is present.</returns>
        public static bool HasBakedLightingFromLightMap(KVObject drawCall)
            => drawCall.ContainsKey("m_bHasBakedLightingFromLightMap")
                && drawCall.GetProperty<bool>("m_bHasBakedLightingFromLightMap");

        /// <summary>
        /// Determines if the draw call has baked lighting from vertex stream.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if baked lighting from vertex stream is present.</returns>
        public static bool HasBakedLightingFromVertexStream(KVObject drawCall)
            => drawCall.ContainsKey("m_bHasBakedLightingFromVertexStream")
                && drawCall.GetProperty<bool>("m_bHasBakedLightingFromVertexStream");

        /// <summary>
        /// Determines if the draw call is an occluder.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if the draw call is an occluder.</returns>
        public static bool IsOccluder(KVObject drawCall)
            => drawCall.ContainsKey("m_bIsOccluder")
                && drawCall.GetProperty<bool>("m_bIsOccluder");

        /// <summary>
        /// Loads external morph data from the file loader.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        public void LoadExternalMorphData(IFileLoader fileLoader)
        {
            if (MorphData == null)
            {
                var morphSetPath = Data.GetStringProperty("m_morphSet");
                if (!string.IsNullOrEmpty(morphSetPath))
                {
                    var morphSetResource = fileLoader.LoadFileCompiled(morphSetPath);
                    if (morphSetResource != null)
                    {
                        MorphData = morphSetResource.GetBlockByType(BlockType.MRPH) as Morph;
                    }
                }
            }

            if (MorphData != null)
            {
                MorphData.LoadFlexData(fileLoader);

                //If texture was not loaded, that means that this model doesn't have any valid morph data.
                if (MorphData.TextureResource == null)
                {
                    MorphData = null;
                }
            }
        }
    }
}
