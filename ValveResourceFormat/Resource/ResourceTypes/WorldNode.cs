using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a world node resource.
    /// </summary>
    public class WorldNode : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> SceneObjects
            => Data.GetArray("m_sceneObjects");

        /// <summary>
        /// Layer indices for <see cref="SceneObjects"/>.
        /// For <see cref="AggregateSceneObjects"/> use the dedicated 'm_nLayer' member.
        /// Value may be null if the node has no layer system.
        /// </summary>
        public IReadOnlyList<long> SceneObjectLayerIndices
            => Data.ContainsKey("m_sceneObjectLayerIndices")
                ? Data.GetIntegerArray("m_sceneObjectLayerIndices")
                : null;

        /// <summary>
        /// Gets the aggregate scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> AggregateSceneObjects
            => Data.ContainsKey("m_aggregateSceneObjects")
                ? Data.GetArray("m_aggregateSceneObjects")
                : [];

        /// <summary>
        /// Gets the clutter scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> ClutterSceneObjects
            => Data.ContainsKey("m_clutterSceneObjects")
                ? Data.GetArray("m_clutterSceneObjects")
                : [];

        /// <summary>
        /// Gets the layer names.
        /// </summary>
        public IReadOnlyList<string> LayerNames
            => Data.ContainsKey("m_layerNames")
                ? Data.GetArray<string>("m_layerNames")
                : [];
    }
}
