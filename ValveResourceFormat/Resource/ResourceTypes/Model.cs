using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a model resource containing meshes, skeleton, and animations.
    /// </summary>
    public class Model : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the model name.
        /// </summary>
        public string Name => Data.GetStringProperty("m_name");

        /// <summary>
        /// Gets the key-values data from the model info.
        /// </summary>
        [NotNull]
        public KVObject KeyValues
        {
            get
            {
                cachedKeyValues ??= ParseKeyValuesText();
                cachedKeyValues ??= new KVObject(string.Empty);

                return cachedKeyValues;
            }
        }

        /// <summary>
        /// Gets the skeleton for this model.
        /// </summary>
        public Skeleton Skeleton
        {
            get
            {
                cachedSkeleton ??= Skeleton.FromModelData(Data);
                return cachedSkeleton;
            }
        }

        /// <summary>
        /// Gets the flex controllers for this model.
        /// </summary>
        public FlexController[] FlexControllers
        {
            get
            {
                cachedFlexControllers ??= GetFlexControllers();
                return cachedFlexControllers;
            }
        }

        private List<Animation> CachedAnimations;
        private KVObject cachedKeyValues;
        private Skeleton cachedSkeleton;
        private FlexController[] cachedFlexControllers;
        private List<(Mesh Mesh, int MeshIndex, string Name)> cachedEmbeddedMeshes;

        /// <summary>
        /// Gets the hitbox sets for this model.
        /// </summary>
        public Dictionary<string, Hitbox[]> HitboxSets { get; private set; }

        /// <summary>
        /// Gets the attachments for this model.
        /// </summary>
        public Dictionary<string, Attachment> Attachments { get; private set; }

        private FlexController[] GetFlexControllers()
        {
            if (Resource.GetBlockByType(BlockType.MRPH) is not Morph morph)
            {
                return [];
            }

            var flexControllersData = morph.Data.GetArray("m_FlexControllers");

            var flexControllers = flexControllersData.Select(d =>
            {
                var name = d.GetStringProperty("m_szName");
                var type = d.GetStringProperty("m_szType");
                var min = d.GetFloatProperty("min");
                var max = d.GetFloatProperty("max");
                return new FlexController(name, type, min, max);
            });
            return flexControllers.ToArray();
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            if (Resource.GetBlockByType(BlockType.MDAT) is Mesh mesh)
            {
                HitboxSets = mesh.HitboxSets;
                Attachments = mesh.Attachments;
            }
        }

        /// <summary>
        /// Populates cached flex controller data from an externally loaded morph resource.
        /// </summary>
        /// <param name="morph">The morph data whose flex controllers should be reused.</param>
        public void SetExternalMorphData(Morph morph)
        {
            cachedFlexControllers ??= morph?.FlexControllers;
        }

        /// <summary>
        /// Populates cached mesh-related data (flex controllers, hitboxes, attachments) from an external mesh resource.
        /// </summary>
        /// <param name="mesh">The mesh providing supplemental data.</param>
        public void SetExternalMeshData(Mesh mesh)
        {
            SetExternalMorphData(mesh.MorphData);

            HitboxSets ??= mesh.HitboxSets;
            Attachments ??= mesh.Attachments;
        }

        /// <summary>
        /// Get the bone remap table of a specific mesh.
        /// This is used to remap bone indices in the mesh VBIB to bone indices of the model skeleton.
        /// </summary>
        public int[] GetRemapTable(int meshIndex)
        {
            var remappingTableStarts = Data.GetIntegerArray("m_remappingTableStarts");

            if (remappingTableStarts.Length <= meshIndex)
            {
                return null;
            }

            var remappingTable = Data.GetIntegerArray("m_remappingTable");

            var remappingTableStart = (int)remappingTableStarts[meshIndex];

            var nextMeshIndex = meshIndex + 1;
            var nextMeshStart = remappingTableStarts.Length > nextMeshIndex
                ? remappingTableStarts[nextMeshIndex]
                : remappingTable.Length;

            var meshBoneCount = nextMeshStart - remappingTableStart;

            var meshRemappingTable = new int[meshBoneCount];
            for (var i = 0; i < meshBoneCount; i++)
            {
                meshRemappingTable[i] = (int)remappingTable[remappingTableStart + i];
            }

            return meshRemappingTable;
        }

        /// <summary>
        /// Gets referenced mesh names and their LoD masks.
        /// </summary>
        /// <returns>Enumerable of mesh index, mesh name, and LoD mask tuples.</returns>
        public IEnumerable<(int MeshIndex, string MeshName, long LoDMask)> GetReferenceMeshNamesAndLoD()
        {
            var refLODGroupMasks = Data.GetIntegerArray("m_refLODGroupMasks");
            var refMeshes = Data.GetArray<string>("m_refMeshes");
            var result = new List<(int MeshIndex, string MeshName, long LoDMask)>(refMeshes.Length);

            for (var meshIndex = 0; meshIndex < refMeshes.Length; meshIndex++)
            {
                var refMesh = refMeshes[meshIndex];

                if (!string.IsNullOrEmpty(refMesh))
                {
                    result.Add((meshIndex, refMesh, refLODGroupMasks[meshIndex]));
                }
            }

            return result;
        }

        /// <summary>
        /// Gets embedded meshes with their LoD masks.
        /// </summary>
        /// <returns>Enumerable of mesh, mesh index, name, and LoD mask tuples.</returns>
        public IEnumerable<(Mesh Mesh, int MeshIndex, string Name, long LoDMask)> GetEmbeddedMeshesAndLoD()
            => GetEmbeddedMeshes().Zip(Data.GetIntegerArray("m_refLODGroupMasks"), (l, r) => (l.Mesh, l.MeshIndex, l.Name, r));

        /// <summary>
        /// Gets embedded meshes from the model.
        /// </summary>
        /// <returns>Enumerable of mesh, mesh index, and name tuples.</returns>
        public IEnumerable<(Mesh Mesh, int MeshIndex, string Name)> GetEmbeddedMeshes()
        {
            if (cachedEmbeddedMeshes != null)
            {
                return cachedEmbeddedMeshes;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedMeshes = ctrl?.Data.GetArray("embedded_meshes");

            if (embeddedMeshes == null)
            {
                cachedEmbeddedMeshes = [];
                return cachedEmbeddedMeshes;
            }

            var meshes = new List<(Mesh Mesh, int MeshIndex, string Name)>(embeddedMeshes.Length);

            foreach (var embeddedMesh in embeddedMeshes)
            {
                if (!embeddedMesh.ContainsKey("vbib_block")) // MVTX MIDX update
                {
                    meshes.Add(ParseEmbeddedMesh2(embeddedMesh));
                    continue;
                }

                var name = embeddedMesh.GetStringProperty("name");
                var meshIndex = (int)embeddedMesh.GetIntegerProperty("mesh_index");
                var dataBlockIndex = (int)embeddedMesh.GetIntegerProperty("data_block");
                var vbibBlockIndex = (int)embeddedMesh.GetIntegerProperty("vbib_block");

                var mesh = Resource.GetBlockByIndex(dataBlockIndex) as Mesh;
                mesh.VBIB = Resource.GetBlockByIndex(vbibBlockIndex) as VBIB;
                mesh.Name = $"{Resource.FileName}:{name}";

                var morphBlockIndex = (int)embeddedMesh.GetIntegerProperty("morph_block");
                if (morphBlockIndex >= 0)
                {
                    mesh.MorphData = Resource.GetBlockByIndex(morphBlockIndex) as Morph;
                }

                meshes.Add((mesh, meshIndex, name));
            }

            cachedEmbeddedMeshes = meshes;
            return cachedEmbeddedMeshes;
        }

        private (Mesh Mesh, int MeshIndex, string Name) ParseEmbeddedMesh2(KVObject embeddedMesh)
        {
            var name = embeddedMesh.GetStringProperty("m_Name");
            var meshIndex = (int)embeddedMesh.GetIntegerProperty("m_nMeshIndex");
            var dataBlockIndex = (int)embeddedMesh.GetIntegerProperty("m_nDataBlock");

            var mesh = Resource.GetBlockByIndex(dataBlockIndex) as Mesh;
            mesh.VBIB = new VBIB(Resource, embeddedMesh);
            mesh.Name = $"{Resource.FileName}:{name}";

            var morphBlockIndex = (int)embeddedMesh.GetIntegerProperty("m_nMorphBlock");
            if (morphBlockIndex >= 0)
            {
                mesh.MorphData = Resource.GetBlockByIndex(morphBlockIndex) as Morph;
            }

            return (mesh, meshIndex, name);
        }

        /// <summary>
        /// Gets embedded physics data from the model.
        /// </summary>
        /// <returns>The physics aggregate data, or null if not present.</returns>
        public PhysAggregateData GetEmbeddedPhys()
        {
            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedPhys = ctrl?.Data.GetSubCollection("embedded_physics");

            if (embeddedPhys == null)
            {
                return null;
            }

            var physBlockIndex = (int)embeddedPhys.GetIntegerProperty("phys_data_block");
            return (PhysAggregateData)Resource.GetBlockByIndex(physBlockIndex);
        }

        /// <summary>
        /// Gets referenced physics data names.
        /// </summary>
        /// <returns>Enumerable of physics data names.</returns>
        public IEnumerable<string> GetReferencedPhysNames()
            => Data.GetArray<string>("m_refPhysicsData");

        /// <summary>
        /// Gets referenced animation group names.
        /// </summary>
        /// <returns>Enumerable of animation group names.</returns>
        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => Data.GetArray<string>("m_refAnimGroups");

        /// <summary>
        /// Gets embedded animations from the model.
        /// </summary>
        /// <returns>Enumerable of animations.</returns>
        public IEnumerable<Animation> GetEmbeddedAnimations()
        {
            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedAnimation = ctrl?.Data.GetSubCollection("embedded_animation");

            if (embeddedAnimation == null)
            {
                return [];
            }

            var groupDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("group_data_block");
            var animDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("anim_data_block");

            var animationGroup = Resource.GetBlockByIndex(groupDataBlockIndex) as KeyValuesOrNTRO;
            var decodeKey = animationGroup.Data.GetSubCollection("m_decodeKey");

            var animationDataBlock = Resource.GetBlockByIndex(animDataBlockIndex) as KeyValuesOrNTRO;

            return Animation.FromData(animationDataBlock.Data, decodeKey, Skeleton, FlexControllers);
        }

        /// <summary>
        /// Gets animations referenced from other models.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <returns>Enumerable of animations.</returns>
        public IEnumerable<Animation> GetReferencedAnimations(IFileLoader fileLoader)
        {
            var refAnimModels = Data.GetArray<string>("m_refAnimIncludeModels");
            if (refAnimModels == null || refAnimModels.Length == 0)
            {
                return [];
            }

            var allAnims = new List<Animation>();
            foreach (var modelName in refAnimModels)
            {
                if (string.IsNullOrEmpty(modelName))
                {
                    continue;
                }

                using var resource = fileLoader.LoadFileCompiled(modelName);
                if (resource == null)
                {
                    continue;
                }

                var model = (Model)resource.DataBlock;
                model.cachedSkeleton = Skeleton;
                var anims = model.GetAllAnimations(fileLoader);
                allAnims.AddRange(anims);
            }

            return allAnims;
        }

        /// <summary>
        /// Gets all animations from this model including embedded, referenced, and animation groups.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <returns>Enumerable of all animations.</returns>
        public IEnumerable<Animation> GetAllAnimations(IFileLoader fileLoader)
        {
            if (CachedAnimations != null)
            {
                return CachedAnimations;
            }

            var animGroupPaths = GetReferencedAnimationGroupNames();
            var animations = GetEmbeddedAnimations().ToList();

            // Load animations from referenced animation groups
            foreach (var animGroupPath in animGroupPaths)
            {
                using var animGroup = fileLoader.LoadFileCompiled(animGroupPath);
                if (animGroup != default)
                {
                    animations.AddRange(AnimationGroupLoader.LoadAnimationGroup(animGroup, fileLoader, Skeleton, FlexControllers));
                }
            }

            var referencedAnims = GetReferencedAnimations(fileLoader);
            animations.AddRange(referencedAnims);

            CachedAnimations = [.. animations];

            return CachedAnimations;
        }

        /// <summary>
        /// Gets the mesh groups defined in the model.
        /// </summary>
        /// <returns>Enumerable of mesh group names.</returns>
        public IEnumerable<string> GetMeshGroups()
            => Data.GetArray<string>("m_meshGroups");

        /// <summary>
        /// Gets the material groups defined in the model.
        /// </summary>
        /// <returns>Enumerable of material group names and their materials.</returns>
        public IEnumerable<(string Name, string[] Materials)> GetMaterialGroups()
           => Data.GetArray<KVObject>("m_materialGroups")
                .Select(group => (group.GetProperty<string>("m_name"), group.GetArray<string>("m_materials")));

        /// <summary>
        /// Gets the default mesh groups based on the default mesh group mask.
        /// </summary>
        /// <returns>Enumerable of default mesh group names.</returns>
        public IEnumerable<string> GetDefaultMeshGroups()
        {
            var defaultGroupMask = Data.GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");

            return GetMeshGroups().Where((group, index) => ((ulong)(1 << index) & defaultGroupMask) != 0);
        }

        KVObject ParseKeyValuesText()
        {
            var keyvaluesString = Data.GetSubCollection("m_modelInfo").GetProperty<string>("m_keyValueText");

            const int NullKeyValuesLengthLimit = 140;
            if (string.IsNullOrEmpty(keyvaluesString)
            || !keyvaluesString.StartsWith("<!-- kv3 ", StringComparison.Ordinal)
            || keyvaluesString.Length < NullKeyValuesLengthLimit)
            {
                return null;
            }

            KVObject keyvalues;
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(keyvaluesString));
            try
            {
                keyvalues = KeyValues3.ParseKVFile(ms).Root;
            }
            catch (Exception e)
            {
                // TODO: Current parser fails when root is "null", so just skip over them for now
                Console.Error.WriteLine(e.ToString());
                return null;
            }

            return keyvalues;
        }

    }
}
