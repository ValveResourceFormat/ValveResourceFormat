using System.Numerics.Tensors;
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
        private static readonly ulong EmittedWrittenFields = ParticleField.CreationTime.Mask();

        /// <summary>The attributes that link a child particle to its parent particle.</summary>
        private static readonly ulong ParentLinkFields = ParticleField.ParentParticleId.Mask() | ParticleField.ParentParticleIndex.Mask();

        private readonly List<int> eventIndices = [];
        private readonly Dictionary<int, int> indexById = [];

        private bool IsSkipping => skipDepth > 0 || (systemState.ParentSystem?.Data?.IsSkipping ?? false);

        /// <summary>Lets the system and its children emit again after a stop. Endcap children keep waiting for an endcap.</summary>
        public void Start()
        {
            systemState.EmissionStopped = false;

            foreach (var emitter in emitters)
            {
                emitter.StartEmission(systemState);
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
        /// endcap. Without an endcap its start age keeps whatever it held before. A frozen system
        /// thaws, and instead of playing an endcap it drops every live particle in the tree.
        /// </remarks>
        internal void StopEmission(bool removeAllParticles, bool playEndCap)
        {
            if (systemState.Frozen)
            {
                systemState.Frozen = false;
                removeAllParticles = true;
                playEndCap = false;
            }

            systemState.EmissionStopped = true;

            if (firstFramePending && !removeAllParticles && !finishedBeforeStartAllowed)
            {
                var root = systemState.Root;
                var wasRunning = root.FunctionsRunning;
                root.FunctionsRunning = true;

                try
                {
                    SimulateFirstFrame();
                }
                finally
                {
                    root.FunctionsRunning = wasRunning;
                }
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
                if (!childSimulation.IsEndCapChild || !playEndCap)
                {
                    childSimulation.StopEmission(removeAllParticles, playEndCap);
                    continue;
                }

                childSimulation.systemState.Age = 0f;
                childSimulation.systemState.EndCapStartAge = 0f;
                childSimulation.EnterEndCap();
                childSimulation.Start();
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
                using var scratch = ParticleScratch.Scope();
                var dying = 0;
                var current = particleCollection.Current;
                var creationTimes = current.Floats(ParticleField.CreationTime);
                var lifetimes = current.ReadFloats(ParticleField.LifeDuration);

                for (var i = 0; i < current.Length; i++)
                {
                    if (systemState.Age - creationTimes[i] + systemState.CurrentStep > lifetimes[i])
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
        /// still alive first. It keeps its seed, the draws run on and the control points
        /// keep what functions wrote to them, unless a pinned seed (<see cref="PinRandomSeed"/>) starts the
        /// draws over and puts those control points back.
        /// </summary>
        public void Replay()
        {
            StopEmission(removeAllParticles: true, playEndCap: false);

            if (pinnedSeed is { } seed)
            {
                systemState.UndoFunctionControlPointWrites();
                ApplySeed(seed, 0);
            }

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

            particlesCreated = 0;
            particlesCreatedThisFrame = 0;
            particlesCreatedThisStep = 0;
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
            previousControlPointsValid = false;
            currentFrameTime = maximumTimeStep;

            systemState.Random.RedrawInputSeeds();
            ResetFunctions();
            NamedValues.Reset();
            ApplyNamedValueLocals();
        }

        /// <summary>
        /// Restarts the system and its children without destroying the particles alive or rewinding the
        /// clock: functions restart their running state and the fade windows count from now. The endcap
        /// ends and instantaneous and maintain emitters resume emitting, but a frozen system stays frozen.
        /// An endcap child goes back to waiting for an endcap.
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
                systemState.OperatorTimeBase = systemState.StartedAge;
            }

            systemState.InEndCap = false;

            foreach (var function in functions)
            {
                function.Restart(systemState);
            }

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.Restart();
            }
        }

        /// <summary>Builds the running state every function starts with.</summary>
        private void ResetFunctions()
        {
            foreach (var function in functions)
            {
                function.Initialize(systemState);
            }
        }

        /// <summary>Advances the system by one frame, in as many substeps as the definition's time step needs.</summary>
        /// <param name="frameTime">Seconds since the last update, scaled by <see cref="ParticleSystemState.TimeScale"/>.</param>
        /// <param name="worldTime">Seconds the containing world has been running.</param>
        public void Update(float frameTime, float worldTime)
        {
            // Only the root carries it; children read it back through their parent chain.
            systemState.WorldTime = worldTime;

            var root = systemState.Root;
            var wasRunning = root.FunctionsRunning;
            root.FunctionsRunning = true;
            using var scratch = ParticleScratch.Scope();

            try
            {
                SimulateInternal(frameTime * systemState.TimeScale);
            }
            finally
            {
                root.FunctionsRunning = wasRunning;
            }
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

            particlesCreatedThisFrame = 0;

            if (frameTime < MinimumFrameTime)
            {
                return;
            }

            var requested = frameTime;

            if (maximumSimTime != 0f && preSimFrames <= minimumFrames)
            {
                if (systemState.Age + frameTime > maximumSimTime)
                {
                    requested = MathF.Max(minimumSimTime, maximumSimTime - systemState.Age);
                }

                preSimFrames++;
            }

            var remaining = MathF.Min(requested, maximumTimeStep * 10f);

            systemState.RequestedAge += remaining;

            if (systemState.RequestedAge >= systemState.StepStartAge && systemState.Age > systemState.RequestedAge)
            {
                remaining = 0f;
            }

            var overshoot = false;

            while (remaining > 0f)
            {
                float step;

                if (remaining > maximumTimeStep)
                {
                    step = maximumTimeStep;
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

                childSimulation.SimulateInternal(frameTime);
            }

            particlesCreated += particlesCreatedThisFrame;
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

            if (overshoot)
            {
                particleCollection.CapturePreviousStep();
            }
            else
            {
                particleCollection.ReleasePreviousStep();
            }

            // Ages are measured from the end of the step
            particleCollection.Time = systemState.Age;

            // Each function that runs displaces the per-particle draws of the ones after it. emitters
            // and initializers inherit whatever the pre-emission walk left behind.
            systemState.Random.ResetOperatorOffset();

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

                using (ParticleScratch.Scope())
                {
                    preEmissionOperator.Operate(systemState, step, strength);
                }

                systemState.Random.AdvanceOperatorOffset();
            }

            Emit(ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE, null);

            systemState.Random.ResetOperatorOffset();

            if (particleCollection.Count > 0)
            {
                foreach (var particleOperator in operators)
                {
                    var strength = particleOperator.GetOperatorRunStrength(systemState);

                    if (strength <= 0.0f)
                    {
                        continue;
                    }

                    using (ParticleScratch.Scope())
                    {
                        particleOperator.Operate(particleCollection, step, systemState, strength);
                    }

                    ApplyKills();

                    if (particleCollection.Count == 0)
                    {
                        break;
                    }

                    systemState.Random.AdvanceOperatorOffset();
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

            systemState.Random.ResetOperatorOffset();

            foreach (var preEmissionOperator in preEmissionOperators)
            {
                var strength = preEmissionOperator.GetOperatorRunStrength(systemState);

                if (strength > 0f)
                {
                    using (ParticleScratch.Scope())
                    {
                        preEmissionOperator.Operate(systemState, systemState.CurrentStep, strength);
                    }

                    UpdatePreviousControlPoints(FirstFrameStep);
                }

                systemState.Random.AdvanceOperatorOffset();
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
            var neededSteps = (int)MathF.Ceiling((to - from) / maximumTimeStep);

            if (neededSteps > MaxPreSimulationSteps)
            {
                logger.LogUniqueWarning(
                    "Effect wants {NeededSteps} pre-simulation substeps, capped at {MaxSteps} {File}",
                    neededSteps, MaxPreSimulationSteps, Name);

                from = to - (MaxPreSimulationSteps * maximumTimeStep);
            }

            systemState.RequestedAge = from;
            skipDepth++;

            try
            {
                for (var time = from; to > time; time += maximumTimeStep)
                {
                    systemState.Age = time;
                    SimulateInternal(maximumTimeStep);
                }
            }
            finally
            {
                skipDepth--;
            }
        }

        /// <summary>
        /// Runs the emitters and initializes what they create. With <see cref="ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE"/>
        /// these are the emitters that run every step; otherwise the ones listening for
        /// <paramref name="eventType"/>, for an event the parent raised on the particles at
        /// <paramref name="parentIndices"/>, and their particles do not count toward the frame.
        /// </summary>
        private void Emit(ParticleEventType eventType, List<int>? parentIndices)
        {
            var forEvent = eventType != ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;
            systemState.EmitterEventParticles = parentIndices;
            particlesCreatedThisStep = 0;

            foreach (var emitter in emitters)
            {
                var listens = forEvent
                    ? (emitter.EventTypeMask & eventType) != 0
                    : emitter.EventTypeMask == ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

                if (!listens)
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

                var count = particleCollection.Count - first;
                InitializeNewParticles(first, count, EmittedWrittenFields, emitter.EmitterIndex);
                particlesCreatedThisStep += count;

                if (!forEvent)
                {
                    particlesCreatedThisFrame += count;
                }
            }

            systemState.EmitterEventParticles = null;
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
            particleCollection.Current.Floats(ParticleField.CreationTime)[index] = creationTime;
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
            particleCollection.Time = systemState.Age;

            var batch = particleCollection.Current.Slice(first, count);

            // Below behavior version 6 the first writer of an attribute wins: a later initializer whose
            // declared write set is already fully initialized is skipped, up to firstMultipleOverride.
            var written = writtenFields;
            systemState.InitializingParticles = true;
            systemState.InitializedFirst = first;
            systemState.SpawnIndexBase = particlesCreated - first;
            systemState.ParticleNumberBase = particlesCreated + particlesCreatedThisStep - first;

            foreach (var initializer in initializers)
            {
                if (initializer.AssociatedEmitterIndex != -1 && initializer.AssociatedEmitterIndex != emitterIndex)
                {
                    continue;
                }

                if (!initializer.ShouldRun(systemState) || initializer.GetOperatorRunStrength(systemState) <= 0f)
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

                systemState.InitializedFields = written;

                FillParticleIds(first, count);

                using (ParticleScratch.Scope())
                {
                    initializer.InitializeParticles(batch, particleCollection, systemState);
                }

                if (initializer.HasRandomInput)
                {
                    AdvanceParticleIds(first, count);
                }

                written |= fields;
            }

            systemState.InitializingParticles = false;
            FillParticleIds(first, count);
            AdvanceParticleIds(first, count);

            particleCollection.SaveInitial(first, count);
            particleCollection.SetPreviousStep(first, count);

            if (raiseEvents)
            {
                Observer?.OnParticlesInitialized(particleCollection, systemState, first, count);
                DispatchSpawnEvents(first, count);
            }
        }

        /// <summary>
        /// Gives the <paramref name="count"/> particles from <paramref name="first"/> the next particle ids,
        /// without taking them from the counter.
        /// </summary>
        private void FillParticleIds(int first, int count)
        {
            var seed = systemState.Random.Seed;
            var particleIds = particleCollection.Current.Integers(ParticleField.ParticleId).Slice(first, count);

            for (var i = 0; i < particleIds.Length; i++)
            {
                particleIds[i] = (particleIdCounter + i + seed) & 0xFFF;
            }
        }

        /// <summary>
        /// Takes the ids <see cref="FillParticleIds"/> gave from the counter. Ids are taken in whole blocks
        /// of four, so the counter also passes the ids of the unused slots up to the end of the last block.
        /// </summary>
        private void AdvanceParticleIds(int first, int count)
        {
            particleIdCounter += MathUtils.AlignUp(first + count, 4) - first;
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

            var current = particleCollection.Current;

            for (var index = first; index < first + count; index++)
            {
                if (current.ReadFloat(ParticleField.LifeDuration, index) > 0f != culled)
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

            if (!particleCollection.HasKills)
            {
                return;
            }

            DispatchEvent(ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED, particleCollection.SortKills());
            RemoveKilledParticles();
        }

        private void DispatchPendingEvents()
        {
            var pending = particleCollection.PendingEvents;

            while (pending.Count > 0)
            {
                var eventType = pending[0].Type;
                var kept = 0;
                eventIndices.Clear();

                for (var i = 0; i < pending.Count; i++)
                {
                    var raised = pending[i];

                    if (raised.Type != eventType)
                    {
                        pending[kept++] = raised;
                    }
                    else if (raised.Index < particleCollection.Count)
                    {
                        eventIndices.Add(raised.Index);
                    }
                }

                pending.RemoveRange(kept, pending.Count - kept);

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
                childSimulation.Emit(eventType, indices);
            }
        }

        /// <summary>Removes every particle at once, without raising events.</summary>
        private void KillAllParticles()
        {
            particleCollection.PendingEvents.Clear();
            particleCollection.Clear();
            systemState.ParticleCount = particleCollection.Count;

            RebindChildParents();
        }

        private void RemoveKilledParticles()
        {
            particleCollection.PruneExpired();
            systemState.ParticleCount = particleCollection.Count;

            RebindChildParents();
        }

        /// <summary>
        /// Points every child particle's <see cref="ParticleField.ParentParticleIndex"/> back at its parent
        /// particle after this system's particles moved, finding the parent by its particle id. A child
        /// particle whose parent is gone has its <see cref="ParticleField.ParentParticleId"/> set to
        /// <see cref="int.MinValue"/>.
        /// </summary>
        private void RebindChildParents()
        {
            var indexBuilt = false;

            foreach (var childSimulation in childSimulations)
            {
                var childParticles = childSimulation.particleCollection.Current;

                // A child that stores no parent links has none to point back, and one that stores only one of them is skipped too
                if ((childSimulation.particleCollection.StoredFields & ParentLinkFields) != ParentLinkFields)
                {
                    continue;
                }

                var parentIds = childParticles.Integers(ParticleField.ParentParticleId);
                var parentIndices = childParticles.Integers(ParticleField.ParentParticleIndex);

                for (var i = 0; i < parentIds.Length; i++)
                {
                    var parentId = parentIds[i];

                    if (parentId == int.MinValue)
                    {
                        continue;
                    }

                    if (!indexBuilt)
                    {
                        BuildIndexById();
                        indexBuilt = true;
                    }

                    if (indexById.TryGetValue(parentId, out var parentIndex))
                    {
                        parentIndices[i] = parentIndex;
                    }
                    else
                    {
                        parentIds[i] = int.MinValue;
                    }
                }
            }
        }

        private void BuildIndexById()
        {
            var particleIds = particleCollection.Current.Integers(ParticleField.ParticleId);
            indexById.Clear();
            indexById.EnsureCapacity(particleIds.Length);

            for (var i = 0; i < particleIds.Length; i++)
            {
                indexById.TryAdd(particleIds[i], i);
            }
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
                using var scratch = ParticleScratch.Scope();
                var particles = particleCollection.Current;
                var radii = particles.ReadFloats(ParticleField.Radius);
                var positions = particles.ReadVectors(ParticleField.Position);

                // Each particle grown by its radius
                for (var i = 0; i < particles.Length; i++)
                {
                    var radius = new Vector3(radii[i]);
                    min = Vector3.Min(min, positions[i] - radius);
                    max = Vector3.Max(max, positions[i] + radius);
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
