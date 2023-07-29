namespace GUI.Types.ParticleRenderer
{
    public enum PfRandomMode
    {
        Invalid = -1,

        /// <summary>
        /// Random per-particle but doesn't change per frame.
        /// </summary>
        Constant = 0,

        /// <summary>
        /// Random per-particle, per-frame.
        /// </summary>
        Varying = 1,
    }
}
