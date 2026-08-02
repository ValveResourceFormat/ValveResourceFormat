using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
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
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    internal class ParticleRenderer
    {
        private readonly List<ParticleFunctionPreEmissionOperator> PreEmissionOperators = [];
        private readonly List<ParticleFunctionEmitter> Emitters = [];

        private readonly List<ParticleFunctionInitializer> Initializers = [];

        private readonly List<ParticleFunctionOperator> Operators = [];

        // Run by C_OP_BasicMovement (not the operator loop): each of its instances asks every force
        // generator to add accelerations into Particle.ForceAccumulator, then integrates and clears it.
        internal readonly List<ParticleFunctionForceGenerator> ForceGenerators = [];

        private readonly List<ParticleFunctionConstraint> Constraints = [];

        private readonly List<ParticleFunctionRenderer> Renderers = [];

        // Caps pre-simulation substeps for pathological content; the largest shipped effect needs 1500
        // (15s at 0.01 step).
        private const int MaxPreSimulationSteps = 2048;

        // Upper bound on constraint work-list rounds per frame (m_nMaxConstraintPasses, default 3).
        // A lone constraint settles in one round; the bound only matters when multiple constraints
        // invalidate each other. ReadConstraintPasses returns 1 for systems with no constraints.
        private readonly int ConstraintPasses;

        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        private readonly Scene scene;

        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        /// <summary>
        /// The scene node this system renders under, when created for one. Renderers use its
        /// per-node lighting bindings.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        public string Name { get; set; }
        public int BehaviorVersion { get; }

        /// <summary>
        /// The group this system belongs to when it is used as a child, matched by
        /// <see cref="PreEmissionOperators.ChooseRandomChildrenInGroup"/> on the parent.
        /// </summary>
        private readonly int GroupId;

        /// <summary>
        /// Whether the parent's child selection currently lets this system run. Always true for a
        /// root system and for children in a group nobody selects from.
        /// </summary>
        private bool ChildEnabled = true;

        private readonly int InitialParticles;
        private readonly int MaxParticles;
        private readonly float MinimumTimeStep;
        private readonly float MaximumTimeStep;

        // The simulation step currently being run; spawn-time velocity encoding (prev = pos - vel*dt)
        // uses it.
        private float currentFrameTime;

        internal float CurrentFrameTime => currentFrameTime;
        private readonly float MinimumSimTime;
        private readonly float MaximumSimTime;
        private readonly int MinimumFrames;
        private readonly float PreSimulationTime;
        private readonly float StopSimulationAfterTime;

        /// <summary>
        /// The particle bounds to use when calculating the bounding box of the particle system.
        /// This is added over the particle's radius value.
        /// </summary>
        private readonly AABB ParticleBoundingBox;

        /// <summary>
        /// Set to true to never cull this particle system.
        /// </summary>
        private readonly bool InfiniteBounds;

        /// <summary>
        /// Cache a reference to <see cref="EmitParticle"/> as to not allocate one for every emitted particle.
        /// </summary>
        private readonly Action<float> emitParticleAction;

        public ControlPoint MainControlPoint
        {
            get => GetControlPoint(0);
            set => systemRenderState.SetControlPoint(0, value);
        }

        public ControlPoint GetControlPoint(int cp) => systemRenderState.GetControlPoint(cp);

        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly RendererContext RendererContext;
        private bool hasStarted;
        private int simulatedFrames;

        private readonly ParticleCollection particleCollection;
        private readonly Dictionary<int, ParticleSnapshot> controlPointSnapshots = [];
        private readonly int snapshotControlPoint;
        private int particlesEmitted;
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, RendererContext rendererContext, Scene scene, ParticleSnapshot? particleSnapshot = null, ParticleSystemRenderState? parentSystemRenderState = null)
        {
            emitParticleAction = EmitParticle;

            childParticleRenderers = [];
            this.RendererContext = rendererContext;
            this.scene = scene;

            var parse = new ParticleDefinitionParser(particleSystem.Data, rendererContext.Logger);
            BehaviorVersion = parse.Int32("m_nBehaviorVersion", 13);
            GroupId = parse.Int32("m_nGroupID", 0);
            InitialParticles = parse.Int32("m_nInitialParticles", 0);
            MaxParticles = parse.Int32("m_nMaxParticles", 1000);
            MinimumTimeStep = parse.Float("m_flMinimumTimeStep", 0f);
            MaximumTimeStep = parse.Float("m_flMaximumTimeStep", 0.1f);
            MinimumSimTime = parse.Float("m_flMinimumSimTime", 0f);
            MaximumSimTime = parse.Float("m_flMaximumSimTime", 0f);
            MinimumFrames = parse.Int32("m_nMinimumFrames", 0);
            PreSimulationTime = parse.Float("m_flPreSimulationTime", 0f);
            StopSimulationAfterTime = parse.Float("m_flStopSimulationAfterTime", 0f);

            MaximumTimeStep = Math.Max(MinimumTimeStep, MaximumTimeStep);
            MaximumSimTime = Math.Max(MinimumSimTime, MaximumSimTime);

            // A zero max timestep would clamp every simulated frame to 0 and freeze the effect; fall back to
            // the 0.1 default instead of treating 0 as "no time passes".
            if (MaximumTimeStep <= 0f)
            {
                MaximumTimeStep = 0.1f;
            }

            currentFrameTime = MaximumTimeStep;

            InfiniteBounds = parse.Boolean("m_bInfiniteBounds", false);
            ParticleBoundingBox = new AABB(
                parse.Vector3("m_BoundingBoxMin", new Vector3(-10)),
                parse.Vector3("m_BoundingBoxMax", new Vector3(10))
            );

            var constantAttributes = new Particle(parse);
            particleCollection = new ParticleCollection(constantAttributes, MaxParticles);

            systemRenderState = new ParticleSystemRenderState(parentSystemRenderState)
            {
                Data = this,
                EndEarly = false
            };

            snapshotControlPoint = parse.Int32("m_nSnapshotControlPoint", 0);

            if (particleSnapshot != null)
            {
                controlPointSnapshots[snapshotControlPoint] = particleSnapshot;
            }
            else if (parse.Data.ContainsKey("m_hSnapshot"))
            {
                var snapshotPath = parse.Data.GetStringProperty("m_hSnapshot");

                if (!string.IsNullOrEmpty(snapshotPath))
                {
                    var snapshotResource = RendererContext.FileLoader.LoadFileCompiled(snapshotPath);

                    if (snapshotResource?.GetBlockByType(BlockType.SNAP) is ParticleSnapshot snap)
                    {
                        controlPointSnapshots[snapshotControlPoint] = snap;
                    }
                }
            }

            Name = particleSystem.Resource?.FileName ?? "<unnamed>";

            SetupFunctions(particleSystem.GetEmitters(), ParticleControllerFactory.TryCreateEmitter, Emitters, "emitter");
            SetupFunctions(particleSystem.GetInitializers(), ParticleControllerFactory.TryCreateInitializer, Initializers, "initializer");
            SetupFunctions(particleSystem.GetForceGenerators(), ParticleControllerFactory.TryCreateForceGenerator, ForceGenerators, "force generator");
            SetupFunctions(particleSystem.GetOperators(), ParticleControllerFactory.TryCreateOperator, Operators, "operator");
            SetupFunctions(particleSystem.GetConstraints(), ParticleControllerFactory.TryCreateConstraint, Constraints, "constraint");
            ConstraintPasses = ReadConstraintPasses(particleSystem);

            SetupRenderers(particleSystem.GetRenderers());

            SetupFunctions(particleSystem.GetPreEmissionOperators(), ParticleControllerFactory.TryCreatePreEmissionOperator, PreEmissionOperators, "pre-emission operator");

            SetupChildParticles(particleSystem.GetChildParticleNames(true));

            CalculateBounds();
        }

        /// <summary>
        /// Gets the particle snapshot associated with the given control point, or null if none exists.
        /// </summary>
        internal ParticleSnapshot? GetControlPointSnapshot(int controlPoint)
        {
            controlPointSnapshots.TryGetValue(controlPoint, out var snap);
            return snap;
        }

        /// <summary>
        /// The live particle states this frame. Read by a child particle system's
        /// <c>C_INIT_CreateFromParentParticles</c> to seed new particles from this system's current positions/velocities.
        /// </summary>
        internal Span<Particle> CurrentParticles => particleCollection.Current;

        /// <summary>
        /// Sets the particle detail tier (0 = Low .. 3 = Ultra) for this system; child systems inherit it.
        /// </summary>
        public void SetDetailLevel(int level)
        {
            systemRenderState.DetailLevel = level;
        }

        public void Start()
        {
            for (var i = 0; i < InitialParticles; ++i)
            {
                EmitParticle(0f);
            }

            foreach (var emitter in Emitters)
            {
                emitter.Start(emitParticleAction);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Start();
            }
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

            // Particle id must be set before initializing because the deterministic randomness uses particle ids
            particleCollection.Current[index].ParticleID = particlesEmitted++;
            particleCollection.Current[index].Index = index;
            particleCollection.Current[index].Position = MainControlPoint.Position;
            particleCollection.Current[index].CreationTime = systemRenderState.Age - ageAtSpawn;
            particleCollection.Current[index].Age = ageAtSpawn;

            foreach (var initializer in Initializers)
            {
                initializer.Initialize(ref particleCollection.Current[index], particleCollection, systemRenderState);
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
            foreach (var emitter in Emitters)
            {
                emitter.Stop();
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Stop();
            }
        }

        public void Restart()
        {
            Stop();

            // Run-once pre-emission operators re-arm, so a re-triggered effect picks fresh children.
            foreach (var preEmissionOperator in PreEmissionOperators)
            {
                preEmissionOperator.HasRun = false;
            }

            systemRenderState.Age = 0;
            systemRenderState.ParticleCount = 0;
            systemRenderState.EndEarly = false;
            particlesEmitted = 0;
            simulatedFrames = 0;
            particleCollection.Clear();
            Start();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }
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
            }
        }

        /// <summary>
        /// Advances the system by one frame. Ordinary frames simulate in a single step; only a frame
        /// longer than <see cref="MaximumTimeStep"/> is broken into substeps, and the total is capped
        /// so a long stall cannot make the system catch up indefinitely. Children advance once per
        /// frame with the raw frame time, each substepping against its own timestep settings.
        /// A system frozen by <see cref="StopSimulationAfterTime"/> holds its children frozen with it.
        /// </summary>
        private void UpdateFrame(float frameTime, bool presimulating)
        {
            if (!hasStarted)
            {
                Start();
                hasStarted = true;

                // Fast-forward the whole m_flPreSimulationTime as fixed maximum-timestep substeps so operators
                // and constraints relax to their settled state before first draw (e.g. a static cable
                // dropping into its droop). One-time at spawn.
                // Renderer updates and bounds are skipped per substep and refreshed once after the burst.
                if (PreSimulationTime > 0f)
                {
                    var step = MaximumTimeStep > 0f ? MaximumTimeStep : PreSimulationTime;
                    var neededSteps = (int)MathF.Ceiling(PreSimulationTime / step);
                    var steps = Math.Min(MaxPreSimulationSteps, neededSteps);

                    if (neededSteps > MaxPreSimulationSteps)
                    {
                        RendererContext.Logger.LogUniqueWarning(
                            "Effect wants {NeededSteps} pre-simulation substeps, capped at {MaxSteps} {File}",
                            neededSteps, MaxPreSimulationSteps, Name);
                    }

                    for (var i = 0; i < steps; i++)
                    {
                        UpdateFrame(step, presimulating: true);
                    }

                    RefreshRenderState();
                }
            }

            var maximumStep = MaximumTimeStep > 0f ? MaximumTimeStep : 0.1f;

            // m_flMaximumSimTime caps the system's total simulated lifetime, measured against the
            // accumulated age; the cap only applies for the first m_nMinimumFrames frames
            if (MaximumSimTime != 0f && simulatedFrames <= MinimumFrames)
            {
                if (systemRenderState.Age + frameTime > MaximumSimTime)
                {
                    frameTime = MathF.Max(MinimumSimTime, MaximumSimTime - systemRenderState.Age);
                }

                simulatedFrames++;
            }

            var remaining = MathF.Min(frameTime, maximumStep * 10f);

            while (remaining > 0f)
            {
                var step = remaining > maximumStep
                    ? maximumStep
                    : MathF.Max(remaining, MinimumTimeStep);

                remaining -= step;

                // Renderer state and bounds are refreshed by the final substep only, as the engine
                // does that bookkeeping once per frame rather than once per step
                Simulate(step, presimulating: presimulating || remaining > 0f);
            }

            if (StopSimulationAfterTime > 0f && systemRenderState.Age >= StopSimulationAfterTime)
            {
                return;
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.ChildEnabled)
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
            if (StopSimulationAfterTime > 0f && systemRenderState.Age >= StopSimulationAfterTime)
            {
                return;
            }

            frameTime = Math.Clamp(frameTime, MinimumTimeStep, MaximumTimeStep);
            currentFrameTime = frameTime;

            systemRenderState.Age += frameTime;

            // Age is not stored by the engine, only derived, so recompute it from the creation
            // stamp before operators read it; particles born later this step carry their own age
            for (var i = 0; i < particleCollection.Count; ++i)
            {
                ref var particle = ref particleCollection.Current[i];
                particle.Age = systemRenderState.Age - particle.CreationTime;
            }

            foreach (var preEmissionOperator in PreEmissionOperators)
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
            }

            foreach (var emitter in Emitters)
            {
                var strength = emitter.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                emitter.Emit(frameTime, systemRenderState, strength);
            }

            foreach (var particleOperator in Operators)
            {
                var strength = particleOperator.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                particleOperator.Operate(particleCollection, frameTime, systemRenderState, strength);
            }

            RunConstraints(frameTime);

            // Remove all dead particles
            particleCollection.PruneExpired();

            particleCollection.PreviousFrameTime = frameTime;

            if (!presimulating)
            {
                foreach (var renderer in Renderers)
                {
                    renderer.Update(particleCollection, systemRenderState);
                }

                CalculateBounds();
            }

            // TODO: Is this the correct place for this because child particle renderers also check this
            if (systemRenderState.EndEarly && systemRenderState.Age > systemRenderState.Duration)
            {
                if (systemRenderState.RestartOnEnd)
                {
                    Restart();
                }
                else
                {
                    Stop();

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

        // Constraints run from a work list bounded by m_nMaxConstraintPasses: each constraint runs once,
        // then is re-run only when a different constraint moved particles this frame. A lone constraint
        // therefore runs once.
        private void RunConstraints(float frameTime)
        {
            if (Constraints.Count == 0)
            {
                return;
            }

            Span<bool> satisfied = Constraints.Count <= 64 ? stackalloc bool[Constraints.Count] : new bool[Constraints.Count];

            for (var pass = 0; pass < ConstraintPasses; pass++)
            {
                var changed = false;

                for (var i = 0; i < Constraints.Count; i++)
                {
                    if (satisfied[i])
                    {
                        continue;
                    }

                    satisfied[i] = true;

                    var constraint = Constraints[i];
                    if (constraint.GetOperatorRunStrength(systemRenderState) <= 0.0f)
                    {
                        continue;
                    }

                    if (constraint.ApplyConstraint(particleCollection, frameTime, systemRenderState))
                    {
                        changed = true;
                        for (var j = 0; j < Constraints.Count; j++)
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

            foreach (var emitter in Emitters)
            {
                if (!emitter.IsFinished)
                {
                    return false;
                }
            }

            foreach (var childRenderer in childParticleRenderers)
            {
                if (childRenderer.ChildEnabled && !childRenderer.IsFinished())
                {
                    return false;
                }
            }

            return true;
        }

        public void Render(Camera camera)
        {
            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.ChildEnabled)
                {
                    continue;
                }

                childParticleRenderer.Render(camera);
            }

            if (particleCollection.Count > 0)
            {
                var rendered = false;

                foreach (var renderer in Renderers)
                {
                    if (renderer.GetOperatorRunStrength(systemRenderState) <= 0.0f)
                    {
                        continue;
                    }

                    renderer.Render(particleCollection, systemRenderState, camera);
                    rendered = true;
                }

                if (rendered)
                {
                    PerfStats.Active.Count(Counter.ParticleSystem);
                }
            }
        }

        /// <summary>
        /// Force-renders each active sub-renderer once with temporary particles.
        /// </summary>
        public void Prewarm(Camera camera)
        {
            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.ChildEnabled)
                {
                    continue;
                }

                childParticleRenderer.Prewarm(camera);
            }

            if (Renderers.Count == 0 || particleCollection.Count > 0)
            {
                return;
            }

            // Cables need 2 particles to render
            const int PrewarmParticleCount = 2;

            // EmitParticle mutates emission-order state that real spawns rely on for determinism -
            // restore it after so these synthetic particles leave no trace once the real simulation starts.
            var savedParticlesEmitted = particlesEmitted;
            var savedParticleCount = systemRenderState.ParticleCount;

            for (var i = 0; i < PrewarmParticleCount; i++)
            {
                EmitParticle(0f);
            }

            foreach (var renderer in Renderers)
            {
                if (renderer.GetOperatorRunStrength(systemRenderState) <= 0.0f)
                {
                    continue;
                }

                renderer.Render(particleCollection, systemRenderState, camera);
            }

            particleCollection.Clear();
            particlesEmitted = savedParticlesEmitted;
            systemRenderState.ParticleCount = savedParticleCount;
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => Renderers
                .SelectMany(static renderer => renderer.GetSupportedRenderModes())
                .Concat(childParticleRenderers.SelectMany(static child => child.GetSupportedRenderModes()));

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in Renderers)
            {
                renderer.SetRenderMode(renderMode);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.SetRenderMode(renderMode);
            }
        }

        // Runs the renderer updates and bounds skipped during the pre-simulation burst; children first so
        // the parent's bounds union sees their settled state.
        private void RefreshRenderState()
        {
            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.RefreshRenderState();
            }

            foreach (var renderer in Renderers)
            {
                renderer.Update(particleCollection, systemRenderState);
            }

            CalculateBounds();
        }

        private void CalculateBounds()
        {
            if (InfiniteBounds)
            {
                return;
            }

            var newBounds = new AABB();
            var hasBounds = false;
            var worldCenter = MainControlPoint.Position;
            var additionalBounds = ParticleBoundingBox;

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
                if (!childParticleRenderer.ChildEnabled)
                {
                    continue;
                }

                newBounds = hasBounds ? newBounds.Union(childParticleRenderer.LocalBoundingBox) : childParticleRenderer.LocalBoundingBox;
                hasBounds = true;
            }

            LocalBoundingBox = newBounds;
        }

        private delegate bool TryCreateFunction<T>(string className, KVObject data, ILogger logger, [MaybeNullWhen(false)] out T result);

        private void SetupFunctions<T>(IEnumerable<KVObject> data, TryCreateFunction<T> tryCreate, List<T> target, string label)
        {
            foreach (var info in data)
            {
                if (IsOperatorDisabled(info, RendererContext.Logger))
                {
                    continue;
                }

                var className = info.GetStringProperty("_class");
                if (tryCreate(className, info, RendererContext.Logger, out var function))
                {
                    target.Add(function);
                }
                else
                {
                    RendererContext.Logger.LogUniqueWarningFor([label, className], UnsupportedClassWarning, label, className, Name);
                }
            }
        }

        // Read m_nMaxConstraintPasses (default 3) so rope springs get enough constraint passes.
        private int ReadConstraintPasses(ParticleSystem particleSystem)
        {
            if (Constraints.Count == 0)
            {
                return 1;
            }

            var passes = 1;
            foreach (var op in particleSystem.GetOperators())
            {
                if (op.GetStringProperty("_class") == "C_OP_BasicMovement")
                {
                    var parse = new ParticleDefinitionParser(op, RendererContext.Logger);
                    passes = Math.Max(passes, parse.Int32("m_nMaxConstraintPasses", 3));
                }
            }

            return passes;
        }

        private void SetupRenderers(IEnumerable<KVObject> rendererData)
        {
            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo, RendererContext.Logger))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetStringProperty("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, RendererContext, scene, out var renderer))
                {
                    Renderers.Add(renderer);
                }
                else
                {
                    RendererContext.Logger.LogUniqueWarningFor(["renderer", rendererClass], UnsupportedClassWarning, "renderer", rendererClass, Name);
                }
            }
        }

        /// <summary>
        /// Suppresses every child system tagged with <paramref name="groupId"/> except for
        /// <paramref name="numberOfChildren"/> randomly chosen ones. Children in other groups are untouched.
        /// </summary>
        internal void ChooseRandomChildrenInGroup(int groupId, int numberOfChildren)
        {
            var remaining = 0;

            foreach (var child in childParticleRenderers)
            {
                if (child.GroupId == groupId)
                {
                    remaining++;
                }
            }

            var needed = Math.Clamp(numberOfChildren, 0, remaining);

            // Selection sampling: walk the group once, taking each candidate with probability
            // needed/remaining. Uniform over all subsets of that size, and needs no scratch buffer.
            foreach (var child in childParticleRenderers)
            {
                if (child.GroupId != groupId)
                {
                    continue;
                }

                var chosen = needed > 0 && Random.Shared.Next(remaining) < needed;
                child.ChildEnabled = chosen;

                if (chosen)
                {
                    needed--;
                }

                remaining--;
            }
        }

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = RendererContext.FileLoader.LoadFileCompiled(childName);

                if (childResource == null)
                {
                    continue;
                }

                var childSystemDefinition = (ParticleSystem?)childResource.DataBlock;
                Debug.Assert(childSystemDefinition != null);

                var childSystem = new ParticleRenderer(childSystemDefinition, RendererContext, scene, null, systemRenderState)
                {
                    MainControlPoint = MainControlPoint
                };

                childParticleRenderers.Add(childSystem);
            }
        }

        private static bool IsOperatorDisabled(KVObject op, ILogger logger)
        {
            var parse = new ParticleDefinitionParser(op, logger);

            // Also skip ops that only run during endcap (currently unsupported)
            return parse.Boolean("m_bDisableOperator", default)
                || parse.Enum<ParticleEndCapMode>("m_nOpEndCapState", default) == ParticleEndCapMode.PARTICLE_ENDCAP_ENDCAP_ON;
        }

        // todo: set this when viewer checkbox is toggled
        public void SetWireframe(bool isWireframe)
        {
            foreach (var renderer in Renderers)
            {
                renderer.SetWireframe(isWireframe);
            }
            foreach (var childRenderer in childParticleRenderers)
            {
                childRenderer.SetWireframe(isWireframe);
            }
        }

        public void Delete()
        {
            foreach (var renderer in Renderers)
            {
                renderer.Delete();
            }

            foreach (var childRenderer in childParticleRenderers)
            {
                childRenderer.Delete();
            }
        }
    }
}
