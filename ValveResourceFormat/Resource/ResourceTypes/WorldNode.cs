using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class WorldNode : KeyValuesOrNTRO
    {
        public IReadOnlyList<IKeyValueCollection> SceneObjects
            => Data.GetArray("m_sceneObjects");

        /// <summary>
        /// Layer indices for <see cref="SceneObjects"/>.
        /// For <see cref="AggregateSceneObjects"/> use the dedicated 'm_nLayer' member. 
        /// </summary>
        public IReadOnlyList<long> SceneObjectLayerIndices
            => Data.ContainsKey("m_sceneObjectLayerIndices")
                ? Data.GetArray<long>("m_sceneObjectLayerIndices")
                : null;

        public IReadOnlyList<IKeyValueCollection> AggregateSceneObjects
            => Data.ContainsKey("m_aggregateSceneObjects")
                ? Data.GetArray("m_aggregateSceneObjects")
                : Array.Empty<IKeyValueCollection>();

        public IReadOnlyList<string> LayerNames
            => Data.ContainsKey("m_layerNames")
                ? Data.GetArray<string>("m_layerNames")
                : Array.Empty<string>();
    }
}
