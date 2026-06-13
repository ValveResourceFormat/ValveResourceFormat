using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a world resource.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/World_t">World_t</seealso>
    public class World : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the entity lump names.
        /// </summary>
        public IReadOnlyCollection<string> GetEntityLumpNames()
            => Data.GetArray<string>("m_entityLumps")!;

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
                .Select(nodeData => nodeData.GetStringProperty("m_worldNodePrefix"))
                .ToList();
    }
}
