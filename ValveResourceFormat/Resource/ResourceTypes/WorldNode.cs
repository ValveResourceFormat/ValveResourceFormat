using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class WorldNode : KeyValuesOrNTRO
    {
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

        public IReadOnlyList<KVObject> AggregateSceneObjects
            => Data.ContainsKey("m_aggregateSceneObjects")
                ? Data.GetArray("m_aggregateSceneObjects")
                : [];

        public IReadOnlyList<KVObject> ClutterSceneObjects
            => Data.ContainsKey("m_clutterSceneObjects")
                ? Data.GetArray("m_clutterSceneObjects")
                : [];

        public IReadOnlyList<string> LayerNames
            => Data.ContainsKey("m_layerNames")
                ? Data.GetArray<string>("m_layerNames")
                : [];
    }
}
