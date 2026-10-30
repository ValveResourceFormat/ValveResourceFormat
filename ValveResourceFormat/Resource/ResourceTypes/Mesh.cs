using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a mesh resource containing geometry and vertex buffer data.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CRenderMesh">CRenderMesh</seealso>
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
                cachedVBIB ??= (VBIB?)Resource.GetBlockByType(BlockType.VBIB) ?? new VBIB(Resource, Data) { Resource = Resource };
                cachedVBIB.EnsureRead();
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
        public string Name { get; set; } = string.Empty;

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
        public Morph? MorphData { get; set; }

        private int? cachedBoneWeightCount;

        /// <summary>
        /// Gets how many bone weights each vertex carries, or zero when the mesh is not skinned.
        /// </summary>
        /// <remarks>
        /// Read off the vertex buffers rather than the skeleton's own <c>m_nBoneWeightCount</c>. A buffer
        /// with blend indices but no weights is skinned to one bone per vertex.
        /// </remarks>
        public int BoneWeightCount => cachedBoneWeightCount ??= ReadBoneWeightCount();

        private int ReadBoneWeightCount()
        {
            var declared = Data.GetSubCollection("m_skeleton")?.GetInt32Property("m_nBoneWeightCount") ?? 0;

            if (declared == 0)
            {
                return 0;
            }

            foreach (var buffer in VBIB.VertexBuffers)
            {
                foreach (var field in buffer.InputLayoutFields)
                {
                    if (field.SemanticName == "BLENDINDICES")
                    {
                        return declared;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads the inverse bind pose of every bone of the mesh's own render skeleton.
        /// </summary>
        /// <returns>One matrix per mesh bone, or <see langword="null"/> when the mesh has no skeleton.</returns>
        public Matrix4x4[]? ReadInverseBindPose()
        {
            var bones = Data.GetSubCollection("m_skeleton")?.GetArray("m_bones");
            if (bones is null)
            {
                return null;
            }

            var inverseBindPose = new Matrix4x4[bones.Count];

            for (var i = 0; i < inverseBindPose.Length; i++)
            {
                inverseBindPose[i] = bones[i].GetSubCollection("m_invBindPose").ToMatrix4x4();
            }

            return inverseBindPose;
        }

        private VBIB? cachedVBIB { get; set; }

        /// <summary>Drops the decoded vertex and index buffer data.</summary>
        public void ReleaseVBIB() => cachedVBIB?.Release();

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

            Name = Resource.FileName ?? string.Empty;

            var attachmentsData = Data.GetArray("m_attachments");
            if (attachmentsData != null)
            {
                for (var i = 0; i < attachmentsData.Count; i++)
                {
                    var attachment = new Attachment(attachmentsData[i]);
                    Attachments.Add(attachment.Name, attachment);
                }
            }

            var hitboxSetsData = Data.GetArray("m_hitboxsets");
            if (hitboxSetsData != null)
            {
                for (var i = 0; i < hitboxSetsData.Count; i++)
                {
                    var hitboxSet = hitboxSetsData[i].GetSubCollection("value") ?? hitboxSetsData[i];
                    var hitboxSetName = hitboxSet.GetStringProperty("m_name");

                    var hitboxesKey = hitboxSet.ContainsKey("m_HitBoxes") ? "m_HitBoxes" : "m_hitboxes";
                    var hitboxes = hitboxSet.GetArray(hitboxesKey).Select(d => new Hitbox(d)).ToArray();

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
            if (sceneObjects.Count == 0)
            {
                MinBounds = MaxBounds = new Vector3(0, 0, 0);
                return;
            }

            var minBounds = sceneObjects[0].GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = sceneObjects[0].GetSubCollection("m_vMaxBounds").ToVector3();

            for (var i = 1; i < sceneObjects.Count; ++i)
            {
                var localMin = sceneObjects[i].GetSubCollection("m_vMinBounds").ToVector3();
                var localMax = sceneObjects[i].GetSubCollection("m_vMaxBounds").ToVector3();

                minBounds = Vector3.Min(minBounds, localMin);
                maxBounds = Vector3.Max(maxBounds, localMax);
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
                return drawCall.GetBooleanProperty("m_bUseCompressedNormalTangent");
            }

            return HasDrawFlag(drawCall, "m_nFlags", RenderMeshDrawPrimitiveFlags.UseCompressedNormalTangent, "MESH_DRAW_FLAGS_USE_COMPRESSED_NORMAL_TANGENT");
        }

        /// <summary>
        /// Determines if a flags value of a draw call has a flag set. A value that holds a single flag is stored
        /// as the name of the flag, any other as a number.
        /// </summary>
        /// <param name="data">The draw call data, or another object that holds draw flags.</param>
        /// <param name="key">The key of the flags value.</param>
        /// <param name="flag">The flag to look for.</param>
        /// <param name="flagName">The name <paramref name="flag"/> has when it is stored as a name.</param>
        /// <returns>True if the flag is set.</returns>
        private static bool HasDrawFlag(KVObject data, string key, RenderMeshDrawPrimitiveFlags flag, string flagName)
        {
            if (!data.TryGetValue(key, out var flags))
            {
                return false;
            }

            if (flags.ValueType == KVValueType.String)
            {
                return ((string)flags).Contains(flagName, StringComparison.Ordinal);
            }

            return ((RenderMeshDrawPrimitiveFlags)(int)flags & flag) != 0;
        }

        /// <summary>
        /// Determines if the draw call has baked lighting from lightmap.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if baked lighting from lightmap is present.</returns>
        public static bool HasBakedLightingFromLightMap(KVObject drawCall)
            => drawCall.GetBooleanProperty("m_bHasBakedLightingFromLightMap");

        /// <summary>
        /// Determines if the draw call has baked lighting from vertex stream.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if baked lighting from vertex stream is present.</returns>
        public static bool HasBakedLightingFromVertexStream(KVObject drawCall)
        {
            if (drawCall.ContainsKey("m_bHasBakedLightingFromVertexStream"))
            {
                return drawCall.GetBooleanProperty("m_bHasBakedLightingFromVertexStream");
            }

            return HasVertexLightingFlag(drawCall, "m_nFlags");
        }

        /// <summary>
        /// Determines if a flags value, such as the one of a draw call, has the compressed per vertex lighting flag set.
        /// </summary>
        /// <param name="data">The object that holds the flags.</param>
        /// <param name="key">The key of the flags value.</param>
        /// <returns>True if the flag is set.</returns>
        internal static bool HasVertexLightingFlag(KVObject data, string key)
            => HasDrawFlag(data, key, RenderMeshDrawPrimitiveFlags.HasBakedLightingFromVertexStream, "MESH_DRAW_FLAGS_USE_COMPRESSED_PER_VERTEX_LIGHTING");

        /// <summary>
        /// Determines if the draw call is an occluder.
        /// </summary>
        /// <param name="drawCall">The draw call data.</param>
        /// <returns>True if the draw call is an occluder.</returns>
        public static bool IsOccluder(KVObject drawCall)
            => drawCall.GetBooleanProperty("m_bIsOccluder");

        /// <summary>
        /// Gets the material a draw call renders with, or <see langword="null"/> when it names none.
        /// </summary>
        /// <param name="drawCall">The draw call to read.</param>
        public static string? GetMaterialName(KVObject drawCall)
        {
            ArgumentNullException.ThrowIfNull(drawCall);

            return drawCall.GetStringProperty("m_material") ?? drawCall.GetStringProperty("m_pMaterial");
        }

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
                        // A standalone vmorf_c keeps the morph set in its DATA block, an embedded one in MRPH.
                        MorphData = (morphSetResource.GetBlockByType(BlockType.MRPH) ?? morphSetResource.DataBlock) as Morph;
                    }
                }
            }

            MorphData?.LoadFlexData(fileLoader);
        }
    }
}
