using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : KeyValuesOrNTRO
    {
        public Skeleton Skeleton
        {
            get
            {
                if (cachedSkeleton == null)
                {
                    cachedSkeleton = Skeleton.FromModelData(Data);
                }
                return cachedSkeleton;
            }
        }
        private List<Animation> CachedAnimations;
        private Skeleton cachedSkeleton { get; set; }
        private readonly IDictionary<(VBIB VBIB, int MeshIndex), VBIB> remappedVBIBCache = new Dictionary<(VBIB VBIB, int MeshIndex), VBIB>();

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
            res = vbib.RemapBoneIndices(VBIB.CombineRemapTables(new int[][] {
                GetRemapTable(meshIndex),
                Skeleton.LocalRemapTable,
            }));
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

                if (!String.IsNullOrEmpty(refMesh))
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
            var embeddedAnimations = new List<Animation>();

            if (!Resource.ContainsBlockType(BlockType.CTRL))
            {
                return embeddedAnimations;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedAnimation = ctrl.Data.GetSubCollection("embedded_animation");

            if (embeddedAnimation == null)
            {
                return embeddedAnimations;
            }

            var groupDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("group_data_block");
            var animDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("anim_data_block");

            var animationGroup = Resource.GetBlockByIndex(groupDataBlockIndex) as KeyValuesOrNTRO;
            var decodeKey = animationGroup.Data.GetSubCollection("m_decodeKey");

            var animationDataBlock = Resource.GetBlockByIndex(animDataBlockIndex) as KeyValuesOrNTRO;

            return Animation.FromData(animationDataBlock.Data, decodeKey, Skeleton);
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
                var animGroup = fileLoader.LoadFile(animGroupPath + "_c");
                if (animGroup != default)
                {
                    animations.AddRange(AnimationGroupLoader.LoadAnimationGroup(animGroup, fileLoader, Skeleton));
                }
            }

            CachedAnimations = animations.ToList();

            return CachedAnimations;
        }

        public IEnumerable<string> GetMeshGroups()
            => Data.GetArray<string>("m_meshGroups");

        public IEnumerable<string> GetMaterialGroups()
           => Data.GetArray<IKeyValueCollection>("m_materialGroups").Select(group => group.GetProperty<string>("m_name"));

        public IEnumerable<string> GetDefaultMeshGroups()
        {
            var defaultGroupMask = Data.GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");

            return GetMeshGroups().Where((group, index) => ((ulong)(1 << index) & defaultGroupMask) != 0);
        }

        public IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
            var groupIndex = GetMeshGroups().ToList().IndexOf(groupName);
            var meshGroupMasks = Data.GetIntegerArray("m_refMeshGroupMasks");
            if (groupIndex >= 0)
            {
                return meshGroupMasks.Select(mask => (mask & 1 << groupIndex) != 0);
            }
            else
            {
                return meshGroupMasks.Select(_ => false);
            }
        }
    }
}
