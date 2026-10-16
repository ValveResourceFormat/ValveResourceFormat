namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Base class for particle constraints. Constraints run inside <see cref="Operators.BasicMovement"/>
    /// after its integration and relax particle positions to satisfy a geometric condition (distance,
    /// rope spring, plane, world collision, ...). They are built from the system's <c>m_Constraints</c>
    /// block.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionConstraint">CParticleFunctionConstraint</seealso>
    abstract class ParticleFunctionConstraint : ParticleFunction
    {
        protected ParticleFunctionConstraint(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>Drops any state the constraint captured from the particles it has seen so far.</summary>
        public virtual void Reset()
        {
        }

        /// <summary>Called when the system restarts; drops the captured state unless overridden.</summary>
        public virtual void Restart(ParticleSystemState particleSystemState) => Reset();

        /// <summary>
        /// Whether the constraint runs once after the relaxation passes of the other constraints
        /// instead of taking part in them.
        /// </summary>
        public virtual bool IsFinalConstraint => false;

        /// <summary>
        /// Applies one relaxation pass of the constraint to the particle collection. Returns true when the
        /// constraint may have moved particles, which the renderer's work list uses to decide whether other
        /// constraints need re-checking this frame.
        /// </summary>
        public abstract bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength);
    }
}
