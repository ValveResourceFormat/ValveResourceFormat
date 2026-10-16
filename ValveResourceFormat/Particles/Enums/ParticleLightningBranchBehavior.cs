namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Which way a lightning branch heads when it splits off its parent bolt.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightnintBranchBehavior_t">ParticleLightnintBranchBehavior_t</seealso>
    public enum ParticleLightningBranchBehavior
    {
        /// <summary>Continues along the direction of the parent segment it splits from.</summary>
        PARTICLE_LIGHTNING_BRANCH_CURRENT_DIR = 0,
        /// <summary>Heads toward the end control point, blended back toward the parent segment by the branch twist.</summary>
        PARTICLE_LIGHTNING_BRANCH_ENDPOINT_DIR = 1,
    }
}
