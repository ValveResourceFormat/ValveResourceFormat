namespace GUI.Types.ParticleRenderer
{
    public enum ParticleFloatRandomMode // ParticleFloatRandomMode_t
    {
        PF_RANDOM_MODE_INVALID = -1,

        /// <summary>
        /// Random per-particle but doesn't change per frame.
        /// </summary>
        PF_RANDOM_MODE_CONSTANT = 0,

        /// <summary>
        /// Random per-particle, per-frame.
        /// </summary>
        PF_RANDOM_MODE_VARYING = 1,
    }
}
