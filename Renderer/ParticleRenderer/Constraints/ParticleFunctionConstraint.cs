namespace ValveResourceFormat.Renderer.Particles.Constraints
{
    /// <summary>
    /// Base class for particle constraints. Constraints run after operators each simulation frame and
    /// relax particle positions to satisfy a geometric condition (distance, rope spring, plane, world
    /// collision, ...). They are built from the system's <c>m_Constraints</c> block.
    /// </summary>
    abstract class ParticleFunctionConstraint : ParticleFunction
    {
        protected ParticleFunctionConstraint(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>
        /// Applies one relaxation pass of the constraint to the particle collection. Returns true when the
        /// constraint may have moved particles, which the renderer's work list uses to decide whether other
        /// constraints need re-checking this frame.
        /// </summary>
        public abstract bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}
