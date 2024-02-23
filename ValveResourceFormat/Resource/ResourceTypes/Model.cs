using System.IO;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : KeyValuesOrNTRO
    {
        public Skeleton Skeleton
        {
            get
            {
                cachedSkeleton ??= Skeleton.FromModelData(Data, filterBonesUsedByLod0: false);
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
        private readonly Dictionary<(VBIB VBIB, int MeshIndex), VBIB> remappedVBIBCache = [];
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

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

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

        public void SetSkeletonFilteredForLod0()
        {
            cachedSkeleton ??= Skeleton.FromModelData(Data, filterBonesUsedByLod0: true);
        }

        public int[] GetRemapTable(int meshIndex)
        {
            var remapTableStarts = Data.GetIntegerArray("m_remappingTableStarts");

            if (remapTableStarts.Length <= meshIndex)
            {
                return null;
            }

            // Get the remap table and invert it for our construction method
            var remapTable = Data.GetIntegerArray("m_remappingTable").Select(i => (int)i);

            var start = (int)remapTableStarts[meshIndex];
            return remapTable
                .Skip(start)
                .Take(Skeleton.LocalRemapTable.Length)
                .ToArray();
        }

        public VBIB RemapBoneIndices(VBIB vbib, int meshIndex)
        {
            if (Skeleton.Bones.Length == 0)
            {
                return vbib;
            }
            if (remappedVBIBCache.TryGetValue((vbib, meshIndex), out var res))
            {
                return res;
            }
            res = vbib.RemapBoneIndices(VBIB.CombineRemapTables([
                GetRemapTable(meshIndex),
                Skeleton.LocalRemapTable,
            ]));
            remappedVBIBCache.Add((vbib, meshIndex), res);
            return res;
        }

        public IEnumerable<(int MeshIndex, string MeshName, long LoDMask)> GetReferenceMeshNamesAndLoD()
        {
            var refLODGroupMasks = Data.GetIntegerArray("m_refLODGroupMasks");
            var refMeshes = Data.GetArray<string>("m_refMeshes");
            var result = new List<(int MeshIndex, string MeshName, long LoDMask)>();

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
            var meshes = new List<(Mesh Mesh, int MeshIndex, string Name)>();

            if (Resource.ContainsBlockType(BlockType.CTRL))
            {
                var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
                var embeddedMeshes = ctrl.Data.GetArray("embedded_meshes");

                if (embeddedMeshes == null)
                {
                    return meshes;
                }

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
            }

            return meshes;
        }

        public PhysAggregateData GetEmbeddedPhys()
        {
            if (!Resource.ContainsBlockType(BlockType.CTRL))
            {
                return null;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedPhys = ctrl.Data.GetSubCollection("embedded_physics");

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
            if (!Resource.ContainsBlockType(BlockType.CTRL))
            {
                return [];
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedAnimation = ctrl.Data.GetSubCollection("embedded_animation");

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
                var animGroup = fileLoader.LoadFileCompiled(animGroupPath);
                if (animGroup != default)
                {
                    animations.AddRange(AnimationGroupLoader.LoadAnimationGroup(animGroup, fileLoader, Skeleton, FlexControllers));
                }
            }

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
