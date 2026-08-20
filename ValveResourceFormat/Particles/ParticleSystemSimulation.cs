using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Emitters;
using ValveResourceFormat.Particles.ForceGenerators;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.PreEmissionOperators;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Runs a particle system: its emitters, initializers, operators, forces, constraints and child
    /// systems, with no graphics backend involved. Whatever draws it watches through <see cref="Observer"/>.
    /// </summary>
    public partial class ParticleSystemSimulation
    {
        private readonly List<ParticleFunctionPreEmissionOperator> preEmissionOperators = [];
        private readonly List<ParticleFunctionEmitter> emitters = [];

        private readonly List<ParticleFunctionInitializer> initializers = [];

        private readonly List<ParticleFunctionOperator> operators = [];

        // Run by C_OP_BasicMovement (not the operator loop): each of its instances asks every force
        // generator to add accelerations into Particle.ForceAccumulator, then integrates and clears it.
        internal readonly List<ParticleFunctionForceGenerator> ForceGenerators = [];

        private readonly List<ParticleFunctionConstraint> constraints = [];

        // Caps pre-simulation substeps for pathological content; the largest shipped effect needs 1500
        // (15s at 0.01 step).
        private const int MaxPreSimulationSteps = 2048;

        // Upper bound on constraint work-list rounds per frame (m_nMaxConstraintPasses, default 3).
        // A lone constraint settles in one round; the bound only matters when multiple constraints
        // invalidate each other. ReadConstraintPasses returns 1 for systems with no constraints.
        private readonly int constraintPasses;

        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        /// <summary>The box containing the system's particles, relative to its first control point.</summary>
        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        /// <summary>
        /// Watches this system so a drawing layer can follow it. Set by whatever draws the system,
        /// and null when nothing does.
        /// </summary>
        public IParticleSystemObserver? Observer { get; set; }

        /// <summary>The file the system was loaded from, used in warnings.</summary>
        public string Name { get; set; }
        /// <summary>The behavior version the definition was authored against, which gates several engine behaviours.</summary>
        public int BehaviorVersion { get; }

        /// <summary>
        /// First initializer list index allowed to overwrite an already-initialized attribute below
        /// behavior version 6 (<c>m_nFirstMultipleOverride_BackwardCompat</c>); -1 applies
        /// first-writer-wins to the whole list.
        /// </summary>
        private readonly int firstMultipleOverride;

        /// <summary>
        /// The group this system belongs to when it is used as a child, matched by
        /// <see cref="ChooseRandomChildrenInGroup"/> on the parent.
        /// </summary>
        private readonly int groupId;

        /// <summary>
        /// Whether the parent's child selection currently lets this system run. Always true for a
        /// root system and for children in a group nobody selects from.
        /// </summary>
        private bool childEnabled = true;

        /// <summary>How long into the parent's life this child waits before starting (<c>m_flDelay</c>).</summary>
        private float startDelay;

        /// <summary>
        /// Whether this child exists only for the parent's endcap (<c>m_bEndCap</c>). It is not started
        /// with the parent, it does not enter the endcap phase itself, and it never holds the parent back
        /// from counting as finished.
        /// </summary>
        private bool isEndCapChild;

        /// <summary>The lowest detail tier this child appears at (<c>m_nDetailLevel</c>).</summary>
        private ParticleDetailLevel detailLevel;

        private readonly int initialParticles;
        private readonly int maxParticles;
        private readonly float minimumTimeStep;
        private readonly float maximumTimeStep;

        // The simulation step currently being run; spawn-time velocity encoding (prev = pos - vel*dt)
        // uses it.
        private float currentFrameTime;

        internal float CurrentFrameTime => currentFrameTime;
        private readonly float minimumSimTime;
        private readonly float maximumSimTime;
        private readonly int minimumFrames;
        private readonly float preSimulationTime;
        private readonly float stopSimulationAfterTime;

        /// <summary>
        /// The particle bounds to use when calculating the bounding box of the particle system.
        /// This is added over the particle's radius value.
        /// </summary>
        private readonly AABB particleBoundingBox;

        /// <summary>
        /// Set to true to never cull this particle system.
        /// </summary>
        private readonly bool infiniteBounds;

        /// <summary>
        /// Cache a reference to <see cref="EmitParticle"/> as to not allocate one for every emitted particle.
        /// </summary>
        private readonly Action<float> emitParticleAction;

        /// <summary>Control point 0, which places the system in the world.</summary>
        public ControlPoint MainControlPoint
        {
            get => GetControlPoint(0);
            set => systemState.SetControlPoint(0, value);
        }

        /// <summary>
        /// Publishes the render camera position to this system and every child for
        /// camera-dependent particle inputs.
        /// </summary>
        public void SetCameraPosition(Vector3 position)
        {
            systemState.CameraPosition = position;

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.SetCameraPosition(position);
            }
        }

        /// <summary>Gets one of the system's control points.</summary>
        /// <param name="cp">The control point index.</param>
        public ControlPoint GetControlPoint(int cp) => systemState.GetControlPoint(cp);

        private readonly List<ParticleSystemSimulation> childSimulations;
        private readonly ILogger logger;
        private readonly IFileLoader fileLoader;
        private bool hasStarted;
        private bool preSimulating;
        private int simulatedFrames;

        /// <summary>
        /// Real time the system has been asked to draw, accumulated across frames. A minimum time step
        /// over-simulates whenever the frame is shorter than it, and the debt is repaid by skipping
        /// later frames until the draw time catches up with the simulated age.
        /// </summary>
        private float targetDrawTime;

        /// <summary>Age at the start of the last simulated step.</summary>
        private float previousSimTime = 1e23f;

        private readonly ParticleCollection particleCollection;
        private int particlesEmitted;
        private ParticleSystemState systemState;

        /// <summary>Builds a runnable system from its definition.</summary>
        /// <param name="particleSystem">The system definition, read through its upgraded tree.</param>
        /// <param name="fileLoader">Resolves the child systems and snapshots the definition names.</param>
        /// <param name="logger">Receives warnings about classes and fields that are not implemented. Pass
        /// nothing to run without any.</param>
        /// <param name="particleSnapshot">A snapshot to publish to the system, when it runs off one.</param>
        /// <param name="parentSystemState">The state of the system running this one as a child.</param>
        public ParticleSystemSimulation(ParticleSystem particleSystem, IFileLoader fileLoader, ILogger? logger = null, ParticleSnapshot? particleSnapshot = null, ParticleSystemState? parentSystemState = null)
        {
            emitParticleAction = EmitParticle;

            childSimulations = [];
            this.logger = logger ?? NullLogger.Instance;
            this.fileLoader = fileLoader;

            var rootData = particleSystem.GetUpgradedData();
            Definition = rootData;
            var parse = new ParticleDefinitionParser(rootData, this.logger);
            BehaviorVersion = parse.Int32("m_nBehaviorVersion", 0);
            parse = parse with { BehaviorVersion = BehaviorVersion };
            firstMultipleOverride = parse.Int32("m_nFirstMultipleOverride_BackwardCompat", -1);
            groupId = parse.Int32("m_nGroupID", 0);
            initialParticles = parse.Int32("m_nInitialParticles", 0);
            maxParticles = parse.Int32("m_nMaxParticles", 1000);

            // The pool is capped whatever the file asks for, and a system that simulates on the GPU
            // is allowed a far larger one
            var particleCeiling = parse.Boolean("m_bIsGPUParticleSystem", false) ? 100000 : 20000;
            maxParticles = Math.Min(maxParticles, particleCeiling);
            minimumTimeStep = parse.Float("m_flMinimumTimeStep", 0f);
            maximumTimeStep = parse.Float("m_flMaximumTimeStep", 0.1f);
            minimumSimTime = parse.Float("m_flMinimumSimTime", 0f);
            maximumSimTime = parse.Float("m_flMaximumSimTime", 0f);
            minimumFrames = parse.Int32("m_nMinimumFrames", 0);
            preSimulationTime = parse.Float("m_flPreSimulationTime", 0f);
            stopSimulationAfterTime = parse.Float("m_flStopSimulationAfterTime", 0f);

            maximumTimeStep = Math.Max(minimumTimeStep, maximumTimeStep);

            // A zero max timestep would clamp every simulated frame to 0 and freeze the effect; fall back to
            // the 0.1 default instead of treating 0 as "no time passes".
            if (maximumTimeStep <= 0f)
            {
                maximumTimeStep = 0.1f;
            }

            currentFrameTime = maximumTimeStep;

            infiniteBounds = parse.Boolean("m_bInfiniteBounds", false);
            particleBoundingBox = new AABB(
                parse.Vector3("m_BoundingBoxMin", new Vector3(-10)),
                parse.Vector3("m_BoundingBoxMax", new Vector3(10))
            );

            var constantAttributes = new Particle(parse);
            particleCollection = new ParticleCollection(constantAttributes, maxParticles);

            systemState = new ParticleSystemState(parentSystemState)
            {
                Data = this,
                EndEarly = false
            };

            snapshotControlPoint = PublishSnapshot(parse, particleSnapshot);

            Name = particleSystem.Resource?.FileName ?? "<unnamed>";

            IReadOnlyList<KVObject> Functions(string key) => rootData.GetArray(key) ?? [];

            SetupFunctions(Functions("m_Emitters"), ParticleControllerFactory.TryCreateEmitter, emitters, "emitter");
            SetupFunctions(Functions("m_Initializers"), ParticleControllerFactory.TryCreateInitializer, initializers, "initializer");
            SetupFunctions(Functions("m_ForceGenerators"), ParticleControllerFactory.TryCreateForceGenerator, ForceGenerators, "force generator");
            SetupFunctions(Functions("m_Operators"), ParticleControllerFactory.TryCreateOperator, operators, "operator");
            SetupFunctions(Functions("m_Constraints"), ParticleControllerFactory.TryCreateConstraint, constraints, "constraint");
            constraintPasses = ReadConstraintPasses(Functions("m_Operators"));

            SetupFunctions(Functions("m_PreEmissionOperators"), ParticleControllerFactory.TryCreatePreEmissionOperator, preEmissionOperators, "pre-emission operator");

            SetupChildParticles(Functions("m_Children"));

            CalculateBounds();
        }

        /// <summary>
        /// The live particle states this frame. Read by a child particle system's
        /// <c>C_INIT_CreateFromParentParticles</c> to seed new particles from this system's current positions/velocities.
        /// </summary>
        internal Span<Particle> CurrentParticles => particleCollection.Current;

        /// <summary>The most particles this system can hold at once.</summary>
        internal int ParticleCapacity => particleCollection.Capacity;

        /// <summary>How many particles the last prune removed from this system.</summary>
        internal int KilledLastPass => particleCollection.KilledLastPass;

        /// <summary>
        /// Sets the particle detail tier for this system; child systems inherit it.
        /// </summary>
        public void SetDetailLevel(ParticleDetailLevel level)
        {
            systemState.DetailLevel = level;
        }


        /// <summary>The systems this one runs as children, in definition order.</summary>
        public IReadOnlyList<ParticleSystemSimulation> Children => childSimulations;

        /// <summary>This system's live particles.</summary>
        public ParticleCollection Particles => particleCollection;

        /// <summary>The state every function of this system reads it through.</summary>
        public ParticleSystemState RenderState => systemState;

        /// <summary>The upgraded definition this system was built from.</summary>
        public KVObject Definition { get; }

        /// <summary>
        /// Spawns throwaway particles so a drawing layer can prime buffers that need some to fill, and
        /// captures the emission state they disturb. Returns null when the system already holds particles.
        /// </summary>
        /// <param name="count">How many particles to spawn.</param>
        public PrewarmScope? BeginPrewarm(int count)
        {
            if (particleCollection.Count > 0)
            {
                return null;
            }

            // EmitParticle moves emission-order state that real spawns rely on for determinism, so
            // EndPrewarm puts it back and these leave no trace once the real simulation starts.
            var scope = new PrewarmScope(particlesEmitted, systemState.ParticleCount);

            for (var i = 0; i < count; i++)
            {
                EmitParticle(0f);
            }

            return scope;
        }

        /// <summary>Drops the particles <see cref="BeginPrewarm"/> spawned and restores emission state.</summary>
        /// <param name="scope">What <see cref="BeginPrewarm"/> returned.</param>
        public void EndPrewarm(PrewarmScope scope)
        {
            particleCollection.Clear();
            particlesEmitted = scope.ParticlesEmitted;
            systemState.ParticleCount = scope.ParticleCount;
        }

        /// <summary>The emission state a prewarm disturbed, handed back to <see cref="EndPrewarm"/>.</summary>
        /// <param name="ParticlesEmitted">Emission counter before the prewarm.</param>
        /// <param name="ParticleCount">Particle count before the prewarm.</param>
        public readonly record struct PrewarmScope(int ParticlesEmitted, long ParticleCount);

        // Runs the renderer updates and bounds skipped during the pre-simulation burst; children first so
        // the parent's bounds union sees their settled state.
        private void RefreshRenderState()
        {
            foreach (var childSimulation in childSimulations)
            {
                childSimulation.RefreshRenderState();
            }

            Observer?.OnFrameSimulated(particleCollection, systemState);

            CalculateBounds();
        }


        private delegate bool TryCreateFunction<T>(string className, KVObject data, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out T result);

        private void SetupFunctions<T>(IEnumerable<KVObject> data, TryCreateFunction<T> tryCreate, List<T> target, string label)
        {
            var definitionIndex = 0;

            foreach (var info in data)
            {
                if (IsOperatorDisabled(info, logger))
                {
                    definitionIndex++;
                    continue;
                }

                var className = info.GetStringProperty("_class");
                if (tryCreate(className, info, logger, BehaviorVersion, out var function))
                {
                    if (function is ParticleFunctionInitializer initializer)
                    {
                        initializer.DefinitionIndex = definitionIndex;
                    }

                    target.Add(function);
                }
                else
                {
                    logger.LogUniqueWarningFor([label, className], UnsupportedClassWarning, label, className, Name);
                }

                definitionIndex++;
            }
        }

        // Read m_nMaxConstraintPasses (default 3) so rope springs get enough constraint passes.
        private int ReadConstraintPasses(IReadOnlyList<KVObject> operatorData)
        {
            if (constraints.Count == 0)
            {
                return 1;
            }

            var passes = 1;
            foreach (var op in operatorData)
            {
                if (op.GetStringProperty("_class") == "C_OP_BasicMovement" && !IsOperatorDisabled(op, logger))
                {
                    var parse = new ParticleDefinitionParser(op, logger, BehaviorVersion);
                    passes = Math.Max(passes, parse.Int32("m_nMaxConstraintPasses", 3));
                }
            }

            return passes;
        }

        private static bool IsOperatorDisabled(KVObject op, ILogger logger)
        {
            var parse = new ParticleDefinitionParser(op, logger);

            return parse.Boolean("m_bDisableOperator", default);
        }

    }
}
