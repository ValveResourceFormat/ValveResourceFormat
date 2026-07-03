using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
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

        private static readonly HashSet<string> loggedWarnings = [];

        private readonly Scene scene;

        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        /// <summary>
        /// The scene node this system renders under, when created for one. Renderers use its
        /// per-node lighting bindings.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        /// <summary>The sun shadow-map depth texture for the current frame, set by the owning scene node.</summary>
        public RenderTexture? SunShadowDepth { get; set; }

        public string Name { get; set; }
        public int BehaviorVersion { get; }

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
        private readonly Action emitParticleAction;

        public ControlPoint MainControlPoint
        {
            get => GetControlPoint(0);
            set => systemRenderState.SetControlPoint(0, value);
        }

        public ControlPoint GetControlPoint(int cp) => systemRenderState.GetControlPoint(cp);

        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly RendererContext RendererContext;
        private bool hasStarted;
        private float accumulatedSimTime;

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
            InitialParticles = parse.Int32("m_nInitialParticles", 0);
            MaxParticles = parse.Int32("m_nMaxParticles", 1000);
            MinimumTimeStep = parse.Float("m_flMinimumTimeStep", 0f);
            MaximumTimeStep = parse.Float("m_flMaximumTimeStep", 0.1f);
            MinimumSimTime = parse.Float("m_flMinimumSimTime", 0f);
            MaximumSimTime = parse.Float("m_flMaximumSimTime", 0f);
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
                EmitParticle();
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

        private void EmitParticle()
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
            particleCollection.Current[index].CreationTime = systemRenderState.Age;

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
            systemRenderState.Age = 0;
            systemRenderState.ParticleCount = 0;
            particlesEmitted = 0;
            particleCollection.Clear();
            Start();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }
        }

        public void Update(float frameTime) => Update(frameTime, presimulating: false);

        private void Update(float frameTime, bool presimulating)
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
                        var message = $"Effect wants {neededSteps} pre-simulation substeps, capped at {MaxPreSimulationSteps}";
                        if (loggedWarnings.Add($"{message} {Name}"))
                        {
                            RendererContext.Logger.LogWarning("{Message} {File}", message, Name);
                        }
                    }

                    for (var i = 0; i < steps; i++)
                    {
                        Update(step, presimulating: true);
                    }

                    RefreshRenderState();
                }
            }

            // Simulation stops after m_flStopSimulationAfterTime and the particles are held
            // in place; a settled static cable freezes here because its pre-simulation already advanced the age
            // to the stop time. Rendering continues from the frozen state.
            if (StopSimulationAfterTime > 0f && systemRenderState.Age >= StopSimulationAfterTime)
            {
                return;
            }

            // Fixed sim time ensures consistent particle aging regardless of client frame rate.
            var useSimTime = MinimumSimTime > 0f || MaximumSimTime > 0f;
            if (useSimTime)
            {
                accumulatedSimTime += frameTime;

                // Skip if below minimum
                if (accumulatedSimTime < MinimumSimTime)
                {
                    return;
                }

                // Clamp if above maximum
                if (MaximumSimTime > 0f && accumulatedSimTime > MaximumSimTime)
                {
                    frameTime = MaximumSimTime;
                    accumulatedSimTime -= MaximumSimTime;
                }
                else
                {
                    frameTime = accumulatedSimTime;
                    accumulatedSimTime = 0f;
                }
            }

            frameTime = Math.Clamp(frameTime, MinimumTimeStep, MaximumTimeStep);
            currentFrameTime = frameTime;

            systemRenderState.Age += frameTime;

            foreach (var preEmissionOperator in PreEmissionOperators)
            {
                preEmissionOperator.Operate(ref systemRenderState, frameTime);
            }

            foreach (var emitter in Emitters)
            {
                var strength = emitter.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                // TODO: Pass in strength
                emitter.Emit(frameTime, systemRenderState);
            }

            foreach (var particleOperator in Operators)
            {
                var strength = particleOperator.GetOperatorRunStrength(systemRenderState);

                if (strength <= 0.0f)
                {
                    continue;
                }

                // TODO: Pass in strength
                particleOperator.Operate(particleCollection, frameTime, systemRenderState);
            }

            RunConstraints(frameTime);

            // Increase age of all particles
            for (var i = 0; i < particleCollection.Count; ++i)
            {
                particleCollection.Current[i].Age += frameTime;
            }

            // Remove all dead particles
            particleCollection.PruneExpired();

            particleCollection.PreviousFrameTime = frameTime;

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Update(frameTime, presimulating);
            }

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
                if (systemRenderState.DestroyInstantlyOnEnd)
                {
                    Restart();
                }
                else
                {
                    Stop();
                }
            }

            systemRenderState.ParticleCount = particleCollection.Count;

            // Control point history feeds control-point velocities; the root snapshots once per step
            // after every consumer (including children, which share the root's control points) has run.
            if (systemRenderState.ParentSystem == null)
            {
                systemRenderState.SnapshotControlPointHistory();
            }
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
                if (!childRenderer.IsFinished())
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
                childParticleRenderer.Render(camera);
            }

            if (particleCollection.Count > 0)
            {
                foreach (var renderer in Renderers)
                {
                    if (renderer.GetOperatorRunStrength(systemRenderState) <= 0.0f)
                    {
                        continue;
                    }

                    renderer.Render(particleCollection, systemRenderState, camera);
                }
            }
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

                var particleBounds = new AABB(pos - radius - additionalBounds.Min, pos + radius + additionalBounds.Max);
                newBounds = hasBounds ? newBounds.Union(particleBounds) : particleBounds;
                hasBounds = true;
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
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
                    LogUniqueUnsupportedWarning(label, className);
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
                    LogUniqueUnsupportedWarning("renderer", rendererClass);
                }
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

        private void LogUniqueUnsupportedWarning(string componentType, string className)
        {
            var message = $"Unsupported {componentType} class '{className}'";
            if (loggedWarnings.Add(message))
            {
                RendererContext.Logger.LogWarning("{Message} {File}", message, Name);
            }
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
