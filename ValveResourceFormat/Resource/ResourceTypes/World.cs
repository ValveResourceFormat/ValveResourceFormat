using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a world resource.
    /// </summary>
    public class World : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the entity lump names.
        /// </summary>
        public IReadOnlyCollection<string> GetEntityLumpNames()
            => Data.GetArray<string>("m_entityLumps");

        /// <summary>
        /// Gets the world lighting information.
        /// </summary>
        public KVObject GetWorldLightingInfo()
            => Data.GetSubCollection("m_worldLightingInfo");

        /// <summary>
        /// Gets the world node names.
        /// </summary>
        public IReadOnlyCollection<string> GetWorldNodeNames()
            => Data.GetArray("m_worldNodes")
                .Select(nodeData => nodeData.GetProperty<string>("m_worldNodePrefix"))
                .ToList();
    }
}
