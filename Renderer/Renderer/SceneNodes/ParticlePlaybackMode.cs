namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Which parts of an effect a <see cref="ParticleSceneNode"/> playback cycle covers.
    /// </summary>
    public enum ParticlePlaybackMode
    {
        /// <summary>The effect alone; the endcap plays only when the effect itself asks for it.</summary>
        Normal,

        /// <summary>The effect, then its endcap once emission has ended.</summary>
        NormalWithEndCap,

        /// <summary>The endcap alone, with emission stopped before the first particle is spawned.</summary>
        EndCapOnly,
    }
}
