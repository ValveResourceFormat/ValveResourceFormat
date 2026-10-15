using System.Runtime.InteropServices;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{

    /// <summary>Emission, the substep loop and the start/stop/restart lifecycle.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>Frame times below this are not simulated at all.</summary>
        private const float MinimumFrameTime = 1e-22f;

        /// <summary>The step length the first frame records for the control points' previous state.</summary>
        private const float FirstFrameStep = 0.05f;

        /// <summary>
        /// Attributes counted as written before the first initializer runs when an emitter creates the
        /// particles: all four emitters set the creation time.
        /// </summary>
        private const ulong EmittedWrittenFields = 1UL << (int)ParticleField.CreationTime;

        private readonly List<int> eventIndices = [];

        private bool IsSkipping => skipDepth > 0 || (systemState.ParentSystem?.Data?.IsSkipping ?? false);

        /// <summary>Lets the system and its children emit again after a stop. Endcap children keep waiting for an endcap.</summary>
        public void Start()
        {
            systemState.EmissionStopped = false;

            foreach (var emitter in emitters)
            {
                emitter.StartEmission();
            }

            foreach (var childSimulation in childSimulations)
            {
                if (!childSimulation.IsEndCapChild)
                {
                    childSimulation.Start();
                }
            }
        }

        /// <summary>
        /// Stops emission on the system and its children, leaving live particles to finish. The system
        /// enters the endcap phase without starting its endcap children.
        /// </summary>
        public void Stop() => StopEmission(removeAllParticles: false, playEndCap: false);

        /// <summary>Stops emission on the system and its children and plays the endcap.</summary>
        public void PlayEndCap() => StopEmission(removeAllParticles: false, playEndCap: true);

        /// <summary>
        /// Stops emission on the system and its children within the current frame, optionally starting
        /// the endcap children and dropping every live particle in the tree.
        /// </summary>
        /// <remarks>
        /// The system enters the endcap phase either way, except an endcap child stopped without an
        /// endcap. Without an endcap its start age keeps whatever it held before.
        /// </remarks>
        internal void StopEmission(bool removeAllParticles, bool playEndCap)
        {
            systemState.EmissionStopped = true;
            systemState.Frozen = false;

            if (firstFramePending && systemState.Age >= 0f)
            {
                SimulateFirstFrame();
            }

            foreach (var emitter in emitters)
            {
                emitter.StopEmission();
            }

            if (removeAllParticles)
            {
                KillAllParticles();
            }

            if (!IsEndCapChild || playEndCap)
            {
                systemState.InEndCap = true;
            }

            if (playEndCap)
            {
                systemState.EndCapStartAge = systemState.Age;
            }

            foreach (var childSimulation in childSimulations)
            {
                if (!childSimulation.IsEndCapChild)
                {
                    childSimulation.StopEmission(removeAllParticles, playEndCap);
                    continue;
                }

                if (playEndCap)
                {
                    childSimulation.systemState.Age = 0f;
                    childSimulation.systemState.EndCapStartAge = 0f;
                    childSimulation.EnterEndCap();
                    childSimulation.Start();
                }
            }
        }

        private void EnterEndCap()
        {
            systemState.InEndCap = true;

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.EnterEndCap();
            }
        }

        /// <summary>
        /// Holds every system below this one in place. <see cref="Operators.EndCapTimedFreeze"/> freezes
        /// its own system directly and the rest of the tree through here.
        /// </summary>
        public void FreezeChildren()
        {
            foreach (var childSimulation in childSimulations)
            {
                childSimulation.systemState.Frozen = true;
                childSimulation.FreezeChildren();
            }
        }

        /// <summary>
        /// Whether the system has run out: no emitter can create more particles and, unless
        /// <paramref name="emissionEndIsEnough"/>, every particle dies within the current step. An
        /// endcap child always counts as finished.
        /// <see cref="PreEmissionOperators.PlayEndCapWhenFinished"/> tests this to decide when to start
        /// the endcap.
        /// </summary>
        public bool HasFinishedEmitting(bool emissionEndIsEnough, bool includeChildren)
        {
            if (IsEndCapChild)
            {
                return true;
            }

            foreach (var emitter in emitters)
            {
                if (emitter.MayCreateMoreParticles(systemState))
                {
                    return false;
                }
            }

            if (!emissionEndIsEnough)
            {
                var dying = 0;

                foreach (ref var particle in particleCollection.Current)
                {
                    if (systemState.Age - particle.CreationTime + systemState.CurrentStep > particle.Lifetime)
                    {
                        dying++;
                    }
                }

                if (dying < particleCollection.Count)
                {
                    return false;
                }
            }

            if (includeChildren)
            {
                foreach (var childSimulation in childSimulations)
                {
                    if (!childSimulation.HasFinishedEmitting(emissionEndIsEnough, includeChildren: true))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Replays the system from scratch, as if it had just been created, discarding the particles
        /// still alive first. The system also takes a fresh random seed.
        /// </summary>
        public void Replay()
        {
            StopEmission(removeAllParticles: true, playEndCap: false);

            systemState.Random.Reseed();
            DeriveChildSeeds();

            Rewind();
            Start();
        }

        /// <summary>
        /// Plays the endcap by itself: the system is rewound and stopped with its endcap before a particle
        /// of its own can live, so only the endcap children play.
        /// </summary>
        public void PlayEndCapOnly()
        {
            Replay();
            StopEmission(removeAllParticles: true, playEndCap: true);
        }

        private void Rewind()
        {
            foreach (var childSimulation in childSimulations)
            {
                childSimulation.Rewind();
            }

            particlesEmitted = 0;
            preSimFrames = 0;
            firstFramePending = true;
            finishedBeforeStartAllowed = false;
            boundsComputed = false;
            boundsValid = false;
            particleCollection.ReleasePreviousStep();

            systemState.Age = systemState.ParentSystem == null ? 0f : IsEndCapChild ? float.MinValue : -startDelay;
            systemState.RequestedAge = 0f;
            systemState.StepStartAge = 1e23f;
            systemState.CurrentStep = 0f;
            systemState.PreviousStep = FirstFrameStep;
            systemState.OperatorTimeBase = 0f;
            systemState.EndCapStartAge = float.MinValue;
            systemState.InEndCap = false;
            systemState.Frozen = false;
            systemState.RewindControlPointChanges();
            previousControlPointsValid = true;
            particleCollection.PreviousFrameTime = FirstFrameStep;

            ResetFunctions();
            NamedValues.Reset();
            ApplyNamedValueLocals();
        }

        /// <summary>
        /// Restarts the system and its children without destroying the particles alive or rewinding the
        /// clock: functions restart their running state and the fade windows count from now. The endcap
        /// ends, but a stopped system stays stopped and a frozen one stays frozen. An endcap child goes
        /// back to waiting for an endcap.
        /// </summary>
        public void Restart()
        {
            preSimFrames = 0;
            systemState.Random.RedrawInputSeeds();
            NamedValues.Recapture();

            if (IsEndCapChild)
            {
                systemState.Age = float.MinValue;
                systemState.OperatorTimeBase = 0f;
            }
            else
            {
                systemState.OperatorTimeBase = systemState.Age == float.MinValue ? 0f : systemState.Age;
            }

            systemState.InEndCap = false;

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                preEmissionOperator.Restart(systemState);
            }

            foreach (var emitter in emitters)
            {
                emitter.Restart(systemState);
            }

            foreach (var initializer in initializers)
            {
                initializer.Restart(systemState);
            }

            foreach (var particleOperator in operators)
            {
                particleOperator.Restart(systemState);
            }

            foreach (var constraint in constraints)
            {
                constraint.Restart(systemState);
            }

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.Restart();
            }
        }

        /// <summary>Builds the running state every function starts with.</summary>
        private void ResetFunctions()
        {
            foreach (var preEmissionOperator in preEmissionOperators)
            {
                preEmissionOperator.Initialize(systemState);
            }

            foreach (var emitter in emitters)
            {
                emitter.Reset();
            }

            foreach (var initializer in initializers)
            {
                initializer.Initialize(systemState);
            }

            foreach (var particleOperator in operators)
            {
                particleOperator.Initialize(systemState);
            }

            foreach (var constraint in constraints)
            {
                constraint.Reset();
            }
        }

        /// <summary>Advances the system by one frame, in as many substeps as the definition's time step needs.</summary>
        /// <param name="frameTime">Seconds since the last update, scaled by <see cref="ParticleSystemState.TimeScale"/>.</param>
        /// <param name="worldTime">Seconds the containing world has been running.</param>
        public void Update(float frameTime, float worldTime)
        {
            // Only the root carries it; children read it back through their parent chain.
            systemState.WorldTime = worldTime;

            SimulateInternal(frameTime * systemState.TimeScale);
        }

        /// <summary>
        /// Advances the system, then its children, by one frame. A frame longer than the maximum time step
        /// is split into steps of that length; a remainder shorter than the minimum time step is
        /// simulated as a full minimum step, and later frames are skipped until real time catches up.
        /// </summary>
        private void SimulateInternal(float frameTime)
        {
            if (systemState.Frozen)
            {
                return;
            }

            NamedValues.BeginFrame();

            if (systemState.Age < 0f)
            {
                if (frameTime >= MinimumFrameTime)
                {
                    systemState.Age += frameTime;
                    UpdatePreviousControlPoints(frameTime);
                }

                return;
            }

            if (preSimulationTime != 0f && firstFramePending)
            {
                SkipToTime(preSimulationTime);

                if (!IsSkipping)
                {
                    RefreshRenderState();
                }

                return;
            }

            if (firstFramePending)
            {
                SimulateFirstFrame();
            }
            else if (!previousControlPointsValid)
            {
                UpdatePreviousControlPoints(systemState.PreviousStep);
            }

            if (frameTime < MinimumFrameTime)
            {
                return;
            }

            var maximumStep = maximumTimeStep > 0f ? maximumTimeStep : 0.1f;
            var requested = frameTime;

            // m_flMaximumSimTime caps the system's total simulated lifetime, measured against the
            // accumulated age; the cap only applies for the first m_nMinimumFrames frames
            if (maximumSimTime != 0f && preSimFrames <= minimumFrames)
            {
                if (systemState.Age + frameTime > maximumSimTime)
                {
                    requested = MathF.Max(minimumSimTime, maximumSimTime - systemState.Age);
                }

                preSimFrames++;
            }

            var remaining = MathF.Min(requested, maximumStep * 10f);

            systemState.RequestedAge += remaining;

            if (systemState.RequestedAge >= systemState.StepStartAge && systemState.Age > systemState.RequestedAge)
            {
                remaining = 0f;
            }

            var overshoot = false;

            while (remaining > 0f)
            {
                float step;

                if (remaining > maximumStep)
                {
                    step = maximumStep;
                }
                else if (minimumTimeStep > remaining)
                {
                    step = minimumTimeStep;
                    overshoot = true;
                }
                else
                {
                    step = remaining;
                }

                remaining -= step;
                SimulateStep(step, overshoot);
            }

            foreach (var childSimulation in childSimulations)
            {
                if (childSimulation.detailLevel > systemState.DetailLevel)
                {
                    continue;
                }

                if (!childSimulation.IsWithinDrawDistance(systemState.CameraPosition))
                {
                    continue;
                }

                childSimulation.SimulateInternal(frameTime);
            }

            UpdatePreviousControlPoints(frameTime);
            CalculateBounds();

            if (systemState.Age > stopSimulationAfterTime)
            {
                systemState.Frozen = true;
            }

            NamedValues.EndFrame();
        }

        private void SimulateStep(float step, bool overshoot)
        {
            if (systemState.CurrentStep != 0f)
            {
                systemState.PreviousStep = systemState.CurrentStep;
            }

            systemState.StepStartAge = systemState.Age;
            systemState.Age += step;
            systemState.CurrentStep = step;

            currentFrameTime = step;
            particleCollection.CurrentFrameTime = step;
            particleCollection.PreviousFrameTime = systemState.PreviousStep;

            if (overshoot)
            {
                particleCollection.CapturePreviousStep();
            }
            else
            {
                particleCollection.ReleasePreviousStep();
            }

            // Age is not stored by the engine, only derived, so recompute it from the creation
            // stamp before operators read it; particles born later this step carry their own age
            foreach (ref var particle in particleCollection.Current)
            {
                particle.Age = systemState.Age - particle.CreationTime;
            }

            // Each function that runs displaces the per-particle draws of the ones after it. emitters
            // and initializers inherit whatever the pre-emission walk left behind.
            systemState.Random.OperatorOffset = 0;

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                if (preEmissionOperator.RunOnce)
                {
                    continue;
                }

                var strength = preEmissionOperator.GetOperatorRunStrength(systemState);

                if (strength <= 0f)
                {
                    continue;
                }

                preEmissionOperator.Operate(ref systemState, step, strength);
                systemState.Random.OperatorOffset += ParticleRandom.OperatorStride;
            }

            EmitAndInit();

            systemState.Random.OperatorOffset = 0;

            if (particleCollection.Count > 0)
            {
                foreach (var particleOperator in operators)
                {
                    var strength = particleOperator.GetOperatorRunStrength(systemState);

                    if (strength <= 0.0f)
                    {
                        continue;
                    }

                    particleOperator.Operate(particleCollection, step, systemState, strength);
                    ApplyKills();

                    if (particleCollection.Count == 0)
                    {
                        break;
                    }

                    systemState.Random.OperatorOffset += ParticleRandom.OperatorStride;
                }
            }

            if (!IsSkipping)
            {
                Observer?.OnFrameSimulated(particleCollection, systemState);
            }
        }

        /// <summary>
        /// The system's first frame, run in the first update its clock is not negative: every
        /// pre-emission operator once, run-once ones included, then the initial particles.
        /// </summary>
        private void SimulateFirstFrame()
        {
            firstFramePending = false;
            systemState.CurrentStep = 0f;
            systemState.StepStartAge = 1e23f;
            preSimFrames = 0;

            if (!previousControlPointsValid)
            {
                UpdatePreviousControlPoints(FirstFrameStep);
            }

            systemState.Random.OperatorOffset = 0;

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                var strength = preEmissionOperator.GetOperatorRunStrength(systemState);

                if (strength > 0f)
                {
                    preEmissionOperator.Operate(ref systemState, systemState.CurrentStep, strength);
                    UpdatePreviousControlPoints(FirstFrameStep);
                }

                systemState.Random.OperatorOffset += ParticleRandom.OperatorStride;
            }

            var initialCount = Math.Min(initialParticles, maxParticles);
            var first = particleCollection.Count;

            for (var i = 0; i < initialCount; i++)
            {
                AddParticle(systemState.Age);
            }

            InitializeNewParticles(first, particleCollection.Count - first, 0, -1);
        }

        private void UpdatePreviousControlPoints(float elapsed)
        {
            systemState.UpdatePreviousControlPoints(elapsed);
            particleCollection.PreviousFrameTime = elapsed;
            previousControlPointsValid = true;
        }

        /// <summary>
        /// Jumps the system's clock forward to <paramref name="time"/> for its pre-simulation. The
        /// emitters then simulate the span they would have emitted over, and the children jump too.
        /// </summary>
        private void SkipToTime(float time)
        {
            if (time <= systemState.Age || (finishedBeforeStartAllowed && systemState.Age == float.MinValue))
            {
                return;
            }

            if (firstFramePending)
            {
                SimulateFirstFrame();
            }

            UpdatePreviousControlPoints(time - systemState.Age);
            systemState.Age = time;
            systemState.RequestedAge = time;

            skipDepth++;

            try
            {
                foreach (var emitter in emitters)
                {
                    emitter.SkipToTime(time, this);
                }

                foreach (var childSimulation in childSimulations)
                {
                    if (!childSimulation.IsEndCapChild || systemState.InEndCap)
                    {
                        childSimulation.SkipToTime(time - childSimulation.startDelay);
                    }
                }
            }
            finally
            {
                skipDepth--;
            }
        }

        /// <summary>
        /// Simulates the system, children included, from <paramref name="from"/> to <paramref name="to"/>
        /// in maximum time steps, for an emitter catching up after <see cref="SkipToTime"/>.
        /// </summary>
        internal void Resimulate(float from, float to)
        {
            var step = maximumTimeStep > 0f ? maximumTimeStep : 0.1f;
            var neededSteps = (int)MathF.Ceiling((to - from) / step);

            if (neededSteps > MaxPreSimulationSteps)
            {
                logger.LogUniqueWarning(
                    "Effect wants {NeededSteps} pre-simulation substeps, capped at {MaxSteps} {File}",
                    neededSteps, MaxPreSimulationSteps, Name);

                from = to - (MaxPreSimulationSteps * step);
            }

            systemState.RequestedAge = from;
            skipDepth++;

            try
            {
                for (var time = from; to > time; time += step)
                {
                    systemState.Age = time;
                    SimulateInternal(step);
                }
            }
            finally
            {
                skipDepth--;
            }
        }

        private void EmitAndInit()
        {
            foreach (var emitter in emitters)
            {
                if (emitter.EventTypeMask != ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE)
                {
                    continue;
                }

                var strength = emitter.GetOperatorRunStrength(systemState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                var first = particleCollection.Count;
                emitter.Emit(systemState, strength, addParticleAction);
                InitializeNewParticles(first, particleCollection.Count - first, EmittedWrittenFields, emitter.EmitterIndex);
            }
        }

        /// <summary>
        /// Runs the emitters listening for <paramref name="eventType"/> for an event the parent raised
        /// on <paramref name="count"/> of its particles.
        /// </summary>
        private void EmitForEvent(ParticleEventType eventType, int count)
        {
            systemState.EmitterEventCount = count;

            foreach (var emitter in emitters)
            {
                if ((emitter.EventTypeMask & eventType) == 0)
                {
                    continue;
                }

                var strength = emitter.GetOperatorRunStrength(systemState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                var first = particleCollection.Count;
                emitter.Emit(systemState, strength, addParticleAction);
                InitializeNewParticles(first, particleCollection.Count - first, EmittedWrittenFields, emitter.EmitterIndex);
            }

            systemState.EmitterEventCount = 0;
        }

        /// <summary>Creates a particle at <paramref name="creationTime"/>, leaving it for <see cref="InitializeNewParticles"/>.</summary>
        private void AddParticle(float creationTime)
        {
            var index = particleCollection.Add();
            if (index < 0)
            {
                return;
            }

            // TODO: Make particle positions and control points local space
            particleCollection.Current[index] = particleCollection.Initial[index];

            // Both ids must be set before initializing, since initializers read them
            particleCollection.Current[index].UniqueParticleId = particlesEmitted++;
            particleCollection.Current[index].ParticleId = particleCollection.Current[index].UniqueParticleId + systemState.Random.Seed;
            particleCollection.Current[index].Index = index;
            particleCollection.Current[index].Position = MainControlPoint.Position;
            particleCollection.Current[index].CreationTime = creationTime;
            particleCollection.Current[index].Age = systemState.Age - creationTime;
        }

        /// <summary>
        /// Runs the initializers over the <paramref name="count"/> particles from <paramref name="first"/>,
        /// one initializer over the whole batch before the next. <paramref name="writtenFields"/> are
        /// the attributes whoever created the particles already wrote, and <paramref name="emitterIndex"/>
        /// the emitter that did, or -1. Unless <paramref name="raiseEvents"/> is off, the observer is told
        /// of the batch and the spawn events go to the children.
        /// </summary>
        private void InitializeNewParticles(int first, int count, ulong writtenFields, int emitterIndex, bool raiseEvents = true)
        {
            if (count <= 0)
            {
                return;
            }

            systemState.ParticleCount = particleCollection.Count;

            var batch = particleCollection.Current.Slice(first, count);

            // Below behavior version 6 the first writer of an attribute wins: a later initializer whose
            // declared write set is already fully initialized is skipped, up to firstMultipleOverride.
            var written = writtenFields;
            systemState.InitializingParticles = true;

            foreach (var initializer in initializers)
            {
                if (initializer.AssociatedEmitterIndex != -1 && initializer.AssociatedEmitterIndex != emitterIndex)
                {
                    continue;
                }

                if (initializer.GetOperatorRunStrength(systemState) <= 0f)
                {
                    continue;
                }

                var fields = initializer.WrittenFields;

                if (BehaviorVersion < 6
                    && (firstMultipleOverride < 0 || initializer.DefinitionIndex < firstMultipleOverride)
                    && (fields & ~written) == 0)
                {
                    continue;
                }

                systemState.Random.BeginFunctionCall();

                foreach (ref var particle in batch)
                {
                    initializer.Initialize(ref particle, particleCollection, systemState);
                }

                written |= fields;
            }

            systemState.InitializingParticles = false;

            for (var index = first; index < first + count; index++)
            {
                ref var emitted = ref particleCollection.Current[index];

                // Snapshot the fully-initialized spawn state into the initial array so operators that scale a
                // particle's initial value (fade out/in, radius interpolation) read the initialized value rather
                // than the default template.
                particleCollection.Initial[index] = emitted;
                particleCollection.SetPreviousStep(index);
            }

            if (raiseEvents)
            {
                Observer?.OnParticlesInitialized(particleCollection, systemState, first, count);
                DispatchSpawnEvents(first, count);
            }
        }

        private void DispatchSpawnEvents(int first, int count)
        {
            var listened = eventListenMask | (Observer?.ObservedEvents ?? ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE);

            if ((listened & (ParticleEventType.PARTICLE_EVENT_TYPE_MASK_SPAWNED | ParticleEventType.PARTICLE_EVENT_TYPE_MASK_CULLED_ON_SPAWN)) == 0)
            {
                return;
            }

            DispatchSpawnEvent(first, count, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_CULLED_ON_SPAWN, culled: true);
            DispatchSpawnEvent(first, count, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_SPAWNED, culled: false);
        }

        private void DispatchSpawnEvent(int first, int count, ParticleEventType eventType, bool culled)
        {
            eventIndices.Clear();

            for (var index = first; index < first + count; index++)
            {
                if (particleCollection.Current[index].Lifetime > 0f != culled)
                {
                    eventIndices.Add(index);
                }
            }

            if (eventIndices.Count > 0)
            {
                DispatchEvent(eventType, eventIndices);
            }
        }

        /// <summary>
        /// Hands the events the last function raised to the observer and the listening children, then
        /// removes the particles it killed. The removed particles are still in place while the
        /// children emit from them.
        /// </summary>
        private void ApplyKills()
        {
            DispatchPendingEvents();

            eventIndices.Clear();
            var current = particleCollection.Current;

            for (var i = 0; i < current.Length; i++)
            {
                if (current[i].MarkedAsKilled)
                {
                    eventIndices.Add(i);
                }
            }

            if (eventIndices.Count == 0)
            {
                return;
            }

            DispatchEvent(ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED, eventIndices);
            RemoveKilledParticles();
        }

        private void DispatchPendingEvents()
        {
            var pending = particleCollection.PendingEvents;

            while (pending.Count > 0)
            {
                var eventType = pending[0].Type;
                eventIndices.Clear();

                for (var i = 0; i < pending.Count; i++)
                {
                    if (pending[i].Type == eventType && pending[i].Index < particleCollection.Count)
                    {
                        eventIndices.Add(pending[i].Index);
                    }
                }

                pending.RemoveAll(raised => raised.Type == eventType);

                if (eventIndices.Count > 0)
                {
                    DispatchEvent(eventType, eventIndices);
                }
            }
        }

        private void DispatchEvent(ParticleEventType eventType, List<int> indices)
        {
            Observer?.OnParticleEvent(particleCollection, eventType, CollectionsMarshal.AsSpan(indices));

            if ((eventListenMask & eventType) == 0)
            {
                return;
            }

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.EmitForEvent(eventType, indices.Count);
            }
        }

        /// <summary>Removes every particle at once, without raising events.</summary>
        private void KillAllParticles()
        {
            particleCollection.PendingEvents.Clear();

            foreach (ref var particle in particleCollection.Current)
            {
                particle.Kill();
            }

            RemoveKilledParticles();
        }

        private void RemoveKilledParticles()
        {
            particleCollection.PruneExpired();
            systemState.ParticleCount = particleCollection.Count;

            RebindChildParents();
        }

        /// <summary>
        /// Points every child particle's <see cref="Particle.ParentParticleIndex"/> back at its parent
        /// particle after this system's particles moved, finding the parent by its particle id. A child
        /// particle whose parent is gone has its <see cref="Particle.ParentParticleId"/> set to
        /// <see cref="int.MinValue"/>.
        /// </summary>
        private void RebindChildParents()
        {
            Dictionary<int, int>? indexById = null;

            foreach (var childSimulation in childSimulations)
            {
                foreach (ref var childParticle in childSimulation.particleCollection.Current)
                {
                    var parentId = childParticle.ParentParticleId;

                    if (parentId == int.MinValue)
                    {
                        continue;
                    }

                    indexById ??= BuildIndexById();

                    if (indexById.TryGetValue(parentId, out var parentIndex))
                    {
                        childParticle.ParentParticleIndex = parentIndex;
                    }
                    else
                    {
                        childParticle.ParentParticleId = int.MinValue;
                    }
                }
            }
        }

        private Dictionary<int, int> BuildIndexById()
        {
            var current = particleCollection.Current;
            var indexById = new Dictionary<int, int>(current.Length);

            for (var i = 0; i < current.Length; i++)
            {
                indexById.TryAdd(current[i].ParticleId, i);
            }

            return indexById;
        }

        /// <summary>
        /// Runs the constraints for <see cref="Operators.BasicMovement"/> once it has moved the particles:
        /// up to <paramref name="maxPasses"/> relaxation passes, in which a constraint runs again only
        /// after another one moved particles, then each final constraint once.
        /// </summary>
        internal void RunConstraints(ParticleCollection particles, float frameTime, int maxPasses)
        {
            if (constraints.Count == 0 || particles.Count == 0)
            {
                return;
            }

            Span<bool> settled = constraints.Count <= 64 ? stackalloc bool[constraints.Count] : new bool[constraints.Count];

            for (var pass = 0; pass < maxPasses; pass++)
            {
                var changed = false;

                for (var i = 0; i < constraints.Count; i++)
                {
                    var constraint = constraints[i];

                    if (settled[i] || constraint.IsFinalConstraint)
                    {
                        continue;
                    }

                    settled[i] = true;

                    var strength = constraint.GetOperatorRunStrength(systemState);

                    if (strength <= 0.0f)
                    {
                        continue;
                    }

                    if (constraint.ApplyConstraint(particles, frameTime, systemState, strength))
                    {
                        changed = true;

                        for (var j = 0; j < constraints.Count; j++)
                        {
                            if (j != i)
                            {
                                settled[j] = false;
                            }
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            foreach (var constraint in constraints)
            {
                if (!constraint.IsFinalConstraint)
                {
                    continue;
                }

                var strength = constraint.GetOperatorRunStrength(systemState);

                if (strength > 0.0f)
                {
                    constraint.ApplyConstraint(particles, frameTime, systemState, strength);
                }
            }
        }

        /// <summary>
        /// Whether the system is done: its first frame has run, it and its children hold no particles,
        /// its clock has started, no emitter can create more, and every child is finished. An endcap
        /// child only counts once its endcap has started.
        /// </summary>
        public bool IsFinished()
        {
            if ((firstFramePending || systemState.Age <= 0f) && !finishedBeforeStartAllowed)
            {
                return false;
            }

            if (HasParticlesInTree())
            {
                return false;
            }

            foreach (var emitter in emitters)
            {
                if (emitter.MayCreateMoreParticles(systemState))
                {
                    return false;
                }
            }

            foreach (var childSimulation in childSimulations)
            {
                if (childSimulation.IsEndCapChild && !childSimulation.systemState.InEndCap)
                {
                    continue;
                }

                if (!childSimulation.IsFinished())
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasParticlesInTree()
            => particleCollection.Count > 0 || childSimulations.Exists(static child => child.HasParticlesInTree());

        private bool boundsComputed;

        private void CalculateBounds()
        {
            boundsComputed = true;

            if (infiniteBounds)
            {
                boundsValid = true;
                return;
            }

            var hasBounds = false;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            if (particleCollection.Count > 0)
            {
                foreach (ref var particle in particleCollection.Current)
                {
                    var radius = new Vector3(particle.Radius);
                    min = Vector3.Min(min, particle.Position - radius);
                    max = Vector3.Max(max, particle.Position + radius);
                }

                min += particleBoundingBox.Min;
                max += particleBoundingBox.Max;
                hasBounds = true;
            }

            foreach (var childSimulation in childSimulations)
            {
                if (!childSimulation.boundsValid || childSimulation.infiniteBounds)
                {
                    continue;
                }

                min = Vector3.Min(min, childSimulation.bounds.Min);
                max = Vector3.Max(max, childSimulation.bounds.Max);
                hasBounds = true;
            }

            boundsValid = hasBounds;

            if (hasBounds)
            {
                bounds = new AABB(min, max);
            }
        }
    }
}
