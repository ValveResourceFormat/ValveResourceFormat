using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class World : KeyValuesOrNTRO
    {
        public IReadOnlyCollection<string> GetEntityLumpNames()
            => Data.GetArray<string>("m_entityLumps");

        public KVObject GetWorldLightingInfo()
            => Data.GetSubCollection("m_worldLightingInfo");

        public IReadOnlyCollection<string> GetWorldNodeNames()
            => Data.GetArray("m_worldNodes")
                .Select(nodeData => nodeData.GetProperty<string>("m_worldNodePrefix"))
                .ToList();
    }
}
