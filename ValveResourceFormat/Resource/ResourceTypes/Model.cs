using System;
using ValveResourceFormat.ResourceTypes.Animation;
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
