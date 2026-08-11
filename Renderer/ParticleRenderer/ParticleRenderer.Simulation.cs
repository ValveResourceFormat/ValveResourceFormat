using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles.Constraints;
using ValveResourceFormat.Renderer.Particles.Emitters;
using ValveResourceFormat.Renderer.Particles.ForceGenerators;
using ValveResourceFormat.Renderer.Particles.Initializers;
using ValveResourceFormat.Renderer.Particles.Operators;
using ValveResourceFormat.Renderer.Particles.PreEmissionOperators;
using ValveResourceFormat.Renderer.Particles.Renderers;
using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{

    /// <summary>Emission, the substep loop and the start/stop/restart lifecycle.</summary>
    internal partial class ParticleRenderer
    {
        /// <summary>Attributes the emit path stamps before initializers run.</summary>
        private const ulong SpawnWrittenFields = 1UL << (int)ParticleField.CreationTime;

        public void Start()
        {
            StartSelf();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Start();
            }

            PreSimulate();
        }

        private void StartSelf()
        {
            for (var i = 0; i < initialParticles; ++i)
            {
                EmitParticle(0f);
            }

            RearmSelf();
        }

        /// <summary>
        /// Resets per-instance function state and re-arms the emitters, without seeding the initial
        /// particle burst. This is what a restart does; only the first start seeds the burst.
        /// </summary>
        private void RearmSelf()
        {
            hasStarted = true;

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                preEmissionOperator.HasRun = false;
                preEmissionOperator.Reset();
            }

            foreach (var initializer in initializers)
            {
                initializer.Reset();
            }

            foreach (var particleOperator in operators)
            {
                particleOperator.Reset();
            }

            foreach (var emitter in emitters)
            {
                emitter.Start(emitParticleAction);
            }
        }

        /// <summary>
        /// Fast-forwards the whole <c>m_flPreSimulationTime</c> as fixed maximum-timestep substeps so
        /// operators and constraints relax to their settled state before first draw (e.g. a static
        /// cable dropping into its droop). Renderer updates and bounds are skipped per substep and
        /// refreshed once after the burst.
        /// </summary>
        private void PreSimulate()
        {
            // A system that restarts within its own pre-simulation window re-enters this through
            // Restart(), so the burst runs only for the outermost call.
            if (preSimulationTime <= 0f || preSimulating)
            {
                return;
            }

            preSimulating = true;

            try
            {
                PreSimulateSteps();
            }
            finally
            {
                preSimulating = false;
            }
        }

        private void PreSimulateSteps()
        {
            var step = maximumTimeStep > 0f ? maximumTimeStep : preSimulationTime;
            var neededSteps = (int)MathF.Ceiling(preSimulationTime / step);
            var steps = Math.Min(MaxPreSimulationSteps, neededSteps);

            if (neededSteps > MaxPreSimulationSteps)
            {
                rendererContext.Logger.LogUniqueWarning(
                    "Effect wants {NeededSteps} pre-simulation substeps, capped at {MaxSteps} {File}",
                    neededSteps, MaxPreSimulationSteps, Name);
            }

            for (var i = 0; i < steps; i++)
            {
                UpdateFrame(step, presimulating: true);
            }

            RefreshRenderState();
        }

        private void EmitParticle(float ageAtSpawn)
        {
            var index = particleCollection.Add();
            if (index < 0)
            {
                return;
            }

            systemRenderState.ParticleCount += 1;

            // TODO: Make particle positions and control points local space
            particleCollection.Current[index] = particleCollection.Initial[index];

            // Both ids must be set before initializing, since initializers read them
            particleCollection.Current[index].UniqueParticleId = particlesEmitted++;
            particleCollection.Current[index].ParticleId = particleCollection.Current[index].UniqueParticleId + systemRenderState.Random.Seed;
            particleCollection.Current[index].Index = index;
            particleCollection.Current[index].Position = MainControlPoint.Position;
            particleCollection.Current[index].CreationTime = systemRenderState.Age - ageAtSpawn;
            particleCollection.Current[index].Age = ageAtSpawn;

            // Below behavior version 6 the first writer of an attribute wins: a later initializer whose
            // declared write set is already fully initialized is skipped, up to firstMultipleOverride.
            var written = SpawnWrittenFields;

            foreach (var initializer in initializers)
            {
                if (!initializer.RunsInCurrentPhase(systemRenderState))
                {
                    continue;
                }

                var fields = initializer.WrittenFields;

                if (BehaviorVersion < 6
                    && (firstMultipleOverride < 0 || initializer.DefinitionIndex < firstMultipleOverride)
                    && fields != 0
                    && (fields & ~written) == 0)
                {
                    continue;
                }

                initializer.Initialize(ref particleCollection.Current[index], particleCollection, systemRenderState);
                written |= fields;
            }

            // The initial velocity is encoded into the Verlet state at spawn (prev = pos - vel*dt);
            // BasicMovement then derives motion purely from the position pair.
            ref var emitted = ref particleCollection.Current[index];
            emitted.PositionPrevious = emitted.Position - (emitted.Velocity * currentFrameTime);

            // Snapshot the fully-initialized spawn state into the initial array so operators that scale a
            // particle's initial value (fade out/in, radius interpolation) read the initialized value rather
            // than the default template.
            particleCollection.Initial[index] = emitted;
        }

        public void Stop()
        {
            foreach (var emitter in emitters)
            {
                emitter.Stop();
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Stop();
            }
        }

        /// <summary>
        /// Starts the endcap on this system and everything below it, stamping each with its own age so
        /// a child's endcap timers run on the child's clock.
        /// </summary>
        public void PlayEndCap()
        {
            systemRenderState.StartEndCap();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                // An endcap child is the effect that plays during the endcap, so it rewinds and starts
                // rather than entering the phase itself; the parent's flag is what lets it run at all.
                if (childParticleRenderer.isEndCapChild)
                {
                    childParticleRenderer.RewindForEndCap();
                    continue;
                }

                childParticleRenderer.PlayEndCap();
            }
        }

        /// <summary>
        /// Returns an endcap child to its unstarted state, so the endcap plays it from the beginning
        /// complete with the initial burst that only a first start seeds.
        /// </summary>
        private void RewindForEndCap()
        {
            ClearParticles();

            hasStarted = false;
            simulatedFrames = 0;
            systemRenderState.EndEarly = false;
            systemRenderState.ClearEndCap();
        }

        /// <summary>
        /// Holds every system below this one in place. <see cref="Operators.EndCapTimedFreeze"/> freezes
        /// its own system directly and the rest of the tree through here.
        /// </summary>
        public void FreezeChildren()
        {
            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.systemRenderState.Frozen = true;
                childParticleRenderer.FreezeChildren();
            }
        }

        /// <summary>
        /// Whether the system has run out: nothing left to emit and, unless
        /// <paramref name="emissionEndIsEnough"/>, no particle that survives another step.
        /// <see cref="PreEmissionOperators.PlayEndCapWhenFinished"/> tests this to decide when to start
        /// the endcap.
        /// </summary>
        public bool HasFinishedEmitting(bool emissionEndIsEnough, bool includeChildren)
        {
            if (systemRenderState.InEndCap || isEndCapChild)
            {
                return true;
            }

            foreach (var emitter in emitters)
            {
                if (!emitter.IsFinished)
                {
                    return false;
                }
            }

            if (!emissionEndIsEnough)
            {
                foreach (ref var particle in particleCollection.Current)
                {
                    if (particle.Lifetime - particle.Age > currentFrameTime)
                    {
                        return false;
                    }
                }
            }

            if (includeChildren)
            {
                foreach (var childParticleRenderer in childParticleRenderers)
                {
                    if (!childParticleRenderer.HasFinishedEmitting(emissionEndIsEnough, includeChildren: true))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Replays the system from scratch, discarding the particles still alive first. This is the
        /// viewer's restart affordance rather than engine behaviour: an effect whose pool is already
        /// saturated would otherwise have no free slots to emit into and appear to do nothing.
        /// </summary>
        public void Replay()
        {
            ClearParticles();
            Restart();
        }

        /// <summary>
        /// Plays the endcap by itself: the system is rewound, emission is stopped before a particle of
        /// its own can live, and anything a pre-simulated system spawned during the rewind is dropped.
        /// Only the endcap children are left to play.
        /// </summary>
        public void PlayEndCapOnly()
        {
            Replay();
            Stop();
            PlayEndCap();
            ClearParticles();
        }

        private void ClearParticles()
        {
            particleCollection.Clear();
            systemRenderState.ParticleCount = 0;
            particlesEmitted = 0;

            // A replay stands in for the effect playing again, so the system takes a fresh random
            // identity along with a rewound clock; no particle survives it
            systemRenderState.Random.Reseed();
            systemRenderState.Age = 0f;
            targetDrawTime = 0f;
            previousSimTime = 1e23f;

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.ClearParticles();
            }
        }

        /// <summary>
        /// Re-arms emission without destroying the particles already alive and without rewinding the
        /// system clock: survivors keep occupying pool slots and keep their real ages, so the next
        /// burst is limited to whatever <c>m_nMaxParticles</c> leaves free.
        /// </summary>
        public void Restart()
        {
            Stop();

            systemRenderState.EndEarly = false;
            systemRenderState.ClearEndCap();
            simulatedFrames = 0;
            RearmSelf();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }

            PreSimulate();
        }

        public void Update(float frameTime, float worldTime)
        {
            // Only the root carries it; children read it back through their parent chain.
            systemRenderState.WorldTime = worldTime;

            UpdateFrame(frameTime, presimulating: false);

            // Control point history feeds control point velocities. The root records it once per frame,
            // after every consumer (including children, which share the root's control points) has run,
            // timed by the real frame rather than by any one system's simulation step.
            if (systemRenderState.ParentSystem == null)
            {
                systemRenderState.SnapshotControlPointHistory(frameTime);
                SnapshotChildControlPointOverrides(frameTime);
            }
        }

        /// <summary>
        /// Advances the system by one frame. Ordinary frames simulate in a single step; only a frame
        /// longer than <see cref="maximumTimeStep"/> is broken into substeps, and the total is capped
        /// so a long stall cannot make the system catch up indefinitely. Children advance once per
        /// frame with the raw frame time, each substepping against its own timestep settings.
        /// A system frozen by <see cref="stopSimulationAfterTime"/> holds its children frozen with it.
        /// </summary>
        private void UpdateFrame(float frameTime, bool presimulating)
        {
            if (!hasStarted)
            {
                Start();
            }

            var maximumStep = maximumTimeStep > 0f ? maximumTimeStep : 0.1f;

            // m_flMaximumSimTime caps the system's total simulated lifetime, measured against the
            // accumulated age; the cap only applies for the first m_nMinimumFrames frames
            if (maximumSimTime != 0f && simulatedFrames <= minimumFrames)
            {
                if (systemRenderState.Age + frameTime > maximumSimTime)
                {
                    frameTime = MathF.Max(minimumSimTime, maximumSimTime - systemRenderState.Age);
                }

                simulatedFrames++;
            }

            var remaining = MathF.Min(frameTime, maximumStep * 10f);

            targetDrawTime += remaining;

            // A frame whose draw time already falls inside the last simulated step is skipped
            if (targetDrawTime >= previousSimTime && systemRenderState.Age > targetDrawTime)
            {
                remaining = 0f;
            }

            while (remaining > 0f)
            {
                var step = remaining > maximumStep
                    ? maximumStep
                    : MathF.Max(remaining, minimumTimeStep);

                remaining -= step;

                // Renderer state and bounds are refreshed by the final substep only, as the engine
                // does that bookkeeping once per frame rather than once per step
                Simulate(step, presimulating: presimulating || remaining > 0f);
            }

            if (stopSimulationAfterTime > 0f && systemRenderState.Age >= stopSimulationAfterTime)
            {
                return;
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.ChildShouldRun(systemRenderState))
                {
                    continue;
                }

                childParticleRenderer.UpdateFrame(frameTime, presimulating);
            }
        }

        private void Simulate(float frameTime, bool presimulating)
        {
            // Simulation stops after m_flStopSimulationAfterTime and the particles are held
            // in place; a settled static cable freezes here because its pre-simulation already advanced the age
            // to the stop time. Rendering continues from the frozen state.
            if (stopSimulationAfterTime > 0f && systemRenderState.Age >= stopSimulationAfterTime)
            {
                return;
            }

            if (systemRenderState.Frozen)
            {
                return;
            }

            // The minimum step is imposed by the substep loop, which applies it only to the final
            // partial step; re-imposing it here would raise every substep
            frameTime = MathF.Min(frameTime, maximumTimeStep);
            currentFrameTime = frameTime;
            particleCollection.CurrentFrameTime = frameTime;

            previousSimTime = systemRenderState.Age;
            systemRenderState.Age += frameTime;

            // Age is not stored by the engine, only derived, so recompute it from the creation
            // stamp before operators read it; particles born later this step carry their own age
            for (var i = 0; i < particleCollection.Count; ++i)
            {
                ref var particle = ref particleCollection.Current[i];
                particle.Age = systemRenderState.Age - particle.CreationTime;
            }

            // Each function that runs displaces the per-particle draws of the ones after it. emitters
            // and initializers inherit whatever the pre-emission walk left behind.
            systemRenderState.Random.OperatorOffset = 0;

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                if (preEmissionOperator.GetOperatorRunStrength(systemRenderState) <= 0f)
                {
                    continue;
                }

                if (preEmissionOperator.RunOnce)
                {
                    if (preEmissionOperator.HasRun)
                    {
                        continue;
                    }

                    preEmissionOperator.HasRun = true;
                }

                preEmissionOperator.Operate(ref systemRenderState, frameTime);
                systemRenderState.Random.OperatorOffset += ParticleRandom.OperatorStride;
            }

            foreach (var emitter in emitters)
            {
                var strength = emitter.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                emitter.Emit(frameTime, systemRenderState, strength);
            }

            systemRenderState.Random.OperatorOffset = 0;

            foreach (var particleOperator in operators)
            {
                var strength = particleOperator.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                particleOperator.Operate(particleCollection, frameTime, systemRenderState, strength);
                systemRenderState.Random.OperatorOffset += ParticleRandom.OperatorStride;
            }

            RunConstraints(frameTime);

            // Remove all dead particles
            particleCollection.PruneExpired();

            particleCollection.PreviousFrameTime = frameTime;

            if (!presimulating)
            {
                foreach (var renderer in renderers)
                {
                    renderer.Update(particleCollection, systemRenderState);
                }

                CalculateBounds();
            }

            // TODO: Is this the correct place for this because child particle renderers also check this
            if (systemRenderState.EndEarly && systemRenderState.Age > systemRenderState.EndTime)
            {
                if (systemRenderState.RestartOnEnd)
                {
                    Restart();
                }
                else
                {
                    Stop();

                    if (systemRenderState.PlayEndCapOnEnd)
                    {
                        PlayEndCap();
                    }

                    // "Destroy immediately" drops the particles still alive; without it they are left
                    // to finish their lifetimes and only emission stops.
                    if (systemRenderState.DestroyInstantlyOnEnd)
                    {
                        particleCollection.Clear();
                    }
                }
            }

            systemRenderState.ParticleCount = particleCollection.Count;
        }

        // constraints run from a work list bounded by m_nMaxConstraintPasses: each constraint runs once,
        // then is re-run only when a different constraint moved particles this frame. A lone constraint
        // therefore runs once.
        private void RunConstraints(float frameTime)
        {
            if (constraints.Count == 0)
            {
                return;
            }

            Span<bool> satisfied = constraints.Count <= 64 ? stackalloc bool[constraints.Count] : new bool[constraints.Count];

            for (var pass = 0; pass < constraintPasses; pass++)
            {
                var changed = false;

                for (var i = 0; i < constraints.Count; i++)
                {
                    if (satisfied[i])
                    {
                        continue;
                    }

                    satisfied[i] = true;

                    var constraint = constraints[i];
                    if (constraint.GetOperatorRunStrength(systemRenderState) <= 0.0f)
                    {
                        continue;
                    }

                    if (constraint.ApplyConstraint(particleCollection, frameTime, systemRenderState))
                    {
                        changed = true;
                        for (var j = 0; j < constraints.Count; j++)
                        {
                            if (j != i)
                            {
                                satisfied[j] = false;
                            }
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        public bool IsFinished()
        {
            if (particleCollection.Count > 0)
            {
                return false;
            }

            foreach (var emitter in emitters)
            {
                if (!emitter.IsFinished)
                {
                    return false;
                }
            }

            foreach (var childRenderer in childParticleRenderers)
            {
                // An endcap child never counts toward whether the system has finished.
                if (childRenderer.isEndCapChild || !childRenderer.childEnabled)
                {
                    continue;
                }

                if (!childRenderer.IsFinished())
                {
                    return false;
                }
            }

            return true;
        }

        private void CalculateBounds()
        {
            if (infiniteBounds)
            {
                return;
            }

            var newBounds = new AABB();
            var hasBounds = false;
            var worldCenter = MainControlPoint.Position;
            var additionalBounds = particleBoundingBox;

            foreach (ref var particle in particleCollection.Current)
            {
                var pos = particle.Position - worldCenter;
                var radius = new Vector3(particle.Radius);

                var particleBounds = new AABB(pos + additionalBounds.Min - radius, pos + additionalBounds.Max + radius);
                newBounds = hasBounds ? newBounds.Union(particleBounds) : particleBounds;
                hasBounds = true;
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.childEnabled)
                {
                    continue;
                }

                newBounds = hasBounds ? newBounds.Union(childParticleRenderer.LocalBoundingBox) : childParticleRenderer.LocalBoundingBox;
                hasBounds = true;
            }

            LocalBoundingBox = newBounds;
        }
    }
}
