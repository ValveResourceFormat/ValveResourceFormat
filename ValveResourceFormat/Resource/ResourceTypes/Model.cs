using System.IO;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : KeyValuesOrNTRO
    {
        public Skeleton Skeleton
        {
            get
            {
                cachedSkeleton ??= Skeleton.FromModelData(Data);
                return cachedSkeleton;
            }
        }
        public FlexController[] FlexControllers
        {
            get
            {
                cachedFlexControllers ??= GetFlexControllers();
                return cachedFlexControllers;
            }
        }

        private List<Animation> CachedAnimations;
        private Skeleton cachedSkeleton { get; set; }
        private FlexController[] cachedFlexControllers { get; set; }
        public Dictionary<string, Hitbox[]> HitboxSets { get; private set; }
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

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            if (Resource.GetBlockByType(BlockType.MDAT) is Mesh mesh)
            {
                HitboxSets = mesh.HitboxSets;
                Attachments = mesh.Attachments;
            }
        }

        public void SetExternalMorphData(Morph morph)
        {
            cachedFlexControllers ??= morph?.FlexControllers;
        }

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

        public IEnumerable<(Mesh Mesh, int MeshIndex, string Name, long LoDMask)> GetEmbeddedMeshesAndLoD()
            => GetEmbeddedMeshes().Zip(Data.GetIntegerArray("m_refLODGroupMasks"), (l, r) => (l.Mesh, l.MeshIndex, l.Name, r));

        public IEnumerable<(Mesh Mesh, int MeshIndex, string Name)> GetEmbeddedMeshes()
        {
            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedMeshes = ctrl?.Data.GetArray("embedded_meshes");

            if (embeddedMeshes == null)
            {
                return [];
            }

            var meshes = new List<(Mesh Mesh, int MeshIndex, string Name)>(embeddedMeshes.Length);

            foreach (var embeddedMesh in embeddedMeshes)
            {
                var name = embeddedMesh.GetStringProperty("name");
                var meshIndex = (int)embeddedMesh.GetIntegerProperty("mesh_index");
                var dataBlockIndex = (int)embeddedMesh.GetIntegerProperty("data_block");
                var vbibBlockIndex = (int)embeddedMesh.GetIntegerProperty("vbib_block");

                var mesh = Resource.GetBlockByIndex(dataBlockIndex) as Mesh;
                mesh.VBIB = Resource.GetBlockByIndex(vbibBlockIndex) as VBIB;

                var morphBlockIndex = (int)embeddedMesh.GetIntegerProperty("morph_block");
                if (morphBlockIndex >= 0)
                {
                    mesh.MorphData = Resource.GetBlockByIndex(morphBlockIndex) as Morph;
                }

                meshes.Add((mesh, meshIndex, name));
            }

            return meshes;
        }

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

        public IEnumerable<string> GetReferencedPhysNames()
            => Data.GetArray<string>("m_refPhysicsData");

        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => Data.GetArray<string>("m_refAnimGroups");

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

        public IEnumerable<string> GetMeshGroups()
            => Data.GetArray<string>("m_meshGroups");

        public IEnumerable<(string Name, string[] Materials)> GetMaterialGroups()
           => Data.GetArray<KVObject>("m_materialGroups")
                .Select(group => (group.GetProperty<string>("m_name"), group.GetArray<string>("m_materials")));

        public IEnumerable<string> GetDefaultMeshGroups()
        {
            var defaultGroupMask = Data.GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");

            return GetMeshGroups().Where((group, index) => ((ulong)(1 << index) & defaultGroupMask) != 0);
        }

        public IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
            var groupIndex = GetMeshGroups().ToList().IndexOf(groupName);
            var meshGroupMasks = Data.GetUnsignedIntegerArray("m_refMeshGroupMasks");
            if (groupIndex >= 0)
            {
                return meshGroupMasks.Select(mask => (mask & 1UL << groupIndex) != 0);
            }
            else
            {
                return meshGroupMasks.Select(_ => false);
            }
        }
    }
}
