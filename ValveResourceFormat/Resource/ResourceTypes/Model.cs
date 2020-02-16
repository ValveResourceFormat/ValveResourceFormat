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

                    meshes.Add(new Mesh(Resource.Blocks[dataBlockIndex] as ResourceData, Resource.Blocks[vbibBlockIndex] as VBIB));
                }
            }

            return meshes;
        }

        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => GetData().GetArray<string>("m_refAnimGroups");

        public IEnumerable<Animation> GetEmbeddedAnimations()
        {
            var animationGroups = new List<Animation>();

            return animationGroups;
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
