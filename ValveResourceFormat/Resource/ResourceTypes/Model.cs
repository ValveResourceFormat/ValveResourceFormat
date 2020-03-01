using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model
    {
        public Resource Resource { get; }

        public Model(Resource modelResource)
        {
            Resource = modelResource;
        }

        public Skeleton GetSkeleton()
        {
            return new Skeleton(GetData());
        }

        public IEnumerable<string> GetReferencedMeshNames()
            => GetData().GetArray<string>("m_refMeshes").Where(m => m != null);

        public IEnumerable<(string MeshName, long LoDMask)> GetReferenceMeshNamesAndLoD()
            => GetReferencedMeshNames().Zip(GetData().GetIntegerArray("m_refLODGroupMasks"), (l, r) => (l, r));

        public IEnumerable<Mesh> GetEmbeddedMeshes()
        {
            var meshes = new List<Mesh>();

            if (Resource.ContainsBlockType(BlockType.CTRL))
            {
                var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
                var embeddedMeshes = ctrl.Data.GetArray("embedded_meshes");

                foreach (var embeddedMesh in embeddedMeshes)
                {
                    var dataBlockIndex = (int)embeddedMesh.GetIntegerProperty("data_block");
                    var vbibBlockIndex = (int)embeddedMesh.GetIntegerProperty("vbib_block");

                    meshes.Add(new Mesh(Resource.GetBlockByIndex(dataBlockIndex) as ResourceData, Resource.GetBlockByIndex(vbibBlockIndex) as VBIB));
                }
            }

            return meshes;
        }

        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => GetData().GetArray<string>("m_refAnimGroups");

        public IEnumerable<Animation> GetEmbeddedAnimations()
        {
            var animations = new List<Animation>();

            if (Resource.ContainsBlockType(BlockType.CTRL))
            {
                var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
                var embeddedAnimation = ctrl.Data.GetSubCollection("embedded_animation");

                if (embeddedAnimation == null)
                {
                    return animations;
                }

                var groupDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("group_data_block");
                var animDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("anim_data_block");

                var animationGroup = new AnimationGroup(Resource.GetBlockByIndex(groupDataBlockIndex) as ResourceData);
                var decodeKey = animationGroup.GetDecodeKey();

                var animationDataBlock = Resource.GetBlockByIndex(animDataBlockIndex) as BinaryKV3;

                animations.AddRange(Animation.FromData(animationDataBlock.Data, decodeKey));
            }

            return animations;
        }

        public IEnumerable<string> GetMeshGroups()
            => GetData().GetArray<string>("m_meshGroups");

        public IEnumerable<string> GetDefaultMeshGroups()
        {
            var defaultGroupMask = GetData().GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");

            return GetMeshGroups().Where((group, index) => ((ulong)(1 << index) & defaultGroupMask) != 0);
        }

        public IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
            var groupIndex = GetMeshGroups().ToList().IndexOf(groupName);
            var meshGroupMasks = GetData().GetIntegerArray("m_refMeshGroupMasks");
            if (groupIndex >= 0)
            {
                return meshGroupMasks.Select(mask => (mask & 1 << groupIndex) != 0);
            }
            else
            {
                return meshGroupMasks.Select(_ => false);
            }
        }

        public IKeyValueCollection GetData()
        {
            var data = Resource.DataBlock;
            if (data is BinaryKV3 binaryKv)
            {
                return binaryKv.Data;
            }
            else if (data is NTRO ntro)
            {
                return ntro.Output;
            }

            throw new InvalidOperationException($"Unknown model data type {data.GetType().Name}");
        }
    }
}
