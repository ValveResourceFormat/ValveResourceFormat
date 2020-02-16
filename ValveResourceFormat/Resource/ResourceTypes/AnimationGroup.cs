using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class AnimationGroup
    {
        private readonly IKeyValueCollection data;

        public AnimationGroup(IKeyValueCollection animationData)
        {
            data = animationData;
        }

        public AnimationGroup(Resource resource)
        {
            var dataBlock = resource.DataBlock;
            data = dataBlock is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)dataBlock).Data;
        }

        public IKeyValueCollection GetDecodeKey()
            => data.GetSubCollection("m_decodeKey");

        public IEnumerable<string> GetAnimationArray()
            => data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
    }
}
