using System;
using ValveResourceFormat.ResourceTypes.Animation;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model
    {
        private readonly Resource resource;

        public Model(Resource modelResource)
        {
            resource = modelResource;
        }

        public Skeleton GetSkeleton()
        {
            return new Skeleton(GetModelData());
        }

        public IKeyValueCollection GetModelData()
        {
            if (resource.Blocks[BlockType.DATA] is BinaryKV3 binaryKv)
            {
                return binaryKv.Data;
            }
            else if (resource.Blocks[BlockType.DATA] is NTRO ntro)
            {
                return ntro.Output;
            }

            throw new InvalidOperationException();
        }
    }
}
