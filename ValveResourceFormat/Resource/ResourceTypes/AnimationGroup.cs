using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class AnimationGroup
    {
        private readonly IKeyValueCollection data;

        public AnimationGroup(ResourceData animationData)
        {
            data = animationData is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)animationData).Data;
        }

        public AnimationGroup(Resource resource)
            : this(resource.DataBlock)
        {
        }

        public IKeyValueCollection GetDecodeKey()
            => data.GetSubCollection("m_decodeKey");

        public IEnumerable<string> GetAnimationArray()
            => data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
    }
}
