using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an animation fetch that specifies a local cycle pose parameter.
    /// </summary>
    public struct AnimationFetch
    {
        /// <summary>
        /// Gets or sets the local cycle pose parameter index.
        /// </summary>
        public int LocalCyclePoseParameter { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationFetch"/> struct.
        /// </summary>
        /// <param name="fetchKV">The KeyValues object containing the fetch data.</param>
        public AnimationFetch(KVObject fetchKV)
        {
            LocalCyclePoseParameter = fetchKV.GetInt32Property("m_nLocalCyclePoseParameter");
        }
    }
}
