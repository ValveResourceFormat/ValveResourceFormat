using System.Diagnostics.CodeAnalysis;
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
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Runs a particle system (emitters, initializers, operators, forces, constraints, children) with no
    /// graphics backend. Drawing layers watch it through <see cref="Observer"/>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleSystemDefinition">CParticleSystemDefinition</seealso>
    public partial class ParticleSystemSimulation
    {
        private readonly List<ParticleFunctionPreEmissionOperator> preEmissionOperators = [];
        private readonly List<ParticleFunctionEmitter> emitters = [];

        private readonly List<ParticleFunctionInitializer> initializers = [];

        private readonly List<ParticleFunctionOperator> operators = [];

        /// <summary>The enabled operators, in the order they run.</summary>
        internal IReadOnlyList<ParticleFunctionOperator> Operators => operators;

        /// <summary>Run by <see cref="BasicMovement"/> rather than the operator loop.</summary>
        internal readonly List<ParticleFunctionForceGenerator> ForceGenerators = [];

        private readonly List<ParticleFunctionConstraint> constraints = [];

        /// <summary>The most substeps a pre-simulation runs; earlier time is skipped.</summary>
        private const int MaxPreSimulationSteps = 2048;

        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        /// <summary>
        /// Classes that only exist in a range of behavior versions, as (class, first version, last
        /// version). Outside its range a function is dropped.
        /// </summary>
        private static readonly (string ClassName, int MinVersion, int MaxVersion)[] ClassVersionRanges =
        [
            ("C_INIT_CreateSequentialPath", int.MinValue, 7),
            ("C_INIT_CreateSequentialPathV2", 8, int.MaxValue),
            ("C_OP_InheritFromParentParticles", int.MinValue, 8),
            ("C_OP_InheritFromParentParticlesV2", 9, int.MaxValue),
            ("C_OP_LockToSavedSequentialPath", int.MinValue, 7),
            ("C_OP_LockToSavedSequentialPathV2", 8, int.MaxValue),
        ];

        private AABB bounds;
        private bool boundsValid;

        /// <summary>
        /// The box containing the system's particles and those of its children, relative to its first
        /// control point. Empty at that control point while there is nothing to bound, and unbounded
        /// before the first frame.
        /// </summary>
        public AABB LocalBoundingBox
        {
            get
            {
                if (infiniteBounds || !boundsComputed)
                {
                    return new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));
                }

                return boundsValid ? bounds.Translate(-MainControlPoint.Position) : new AABB(Vector3.Zero, Vector3.Zero);
            }
        }

        /// <summary>
        /// The world-space box last calculated for the system, kept while it has nothing to bound.
        /// </summary>
        internal AABB WorldBounds => bounds;

        /// <summary>
        /// The world-space box around the system's particles and children while it is valid, or null.
        /// A system with infinite bounds has one from the start, covering everything.
        /// </summary>
        internal AABB? WorldBoundingBox => infiniteBounds
            ? new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue))
            : boundsComputed && boundsValid ? bounds : null;

        /// <summary>
        /// The squared camera distance past which the system is not drawn (<c>m_flMaxDrawDistance</c>),
        /// or <see cref="float.MaxValue"/> when it draws at any distance. Each system in a tree carries
        /// its own, so a child can drop out while its parent still draws.
        /// </summary>
        public float MaxDrawDistanceSquared { get; }

        /// <summary>
        /// Seconds the system keeps simulating after it was last drawn (<c>m_flNoDrawTimeToGoToSleep</c>).
        /// </summary>
        public float NoDrawTimeToGoToSleep { get; }

        /// <summary>
        /// Whether the camera is near enough for the system to draw. Measured to the nearest point of
        /// its bounds rather than to their centre, so a system wide enough to reach the camera is kept
        /// wherever its centre happens to sit.
        /// </summary>
        /// <param name="cameraPosition">The world position to measure from.</param>
        public bool IsWithinDrawDistance(Vector3 cameraPosition)
        {
            if (MaxDrawDistanceSquared == float.MaxValue)
            {
                return true;
            }

            var box = LocalBoundingBox.Translate(MainControlPoint.Position);
            var nearest = Vector3.Clamp(cameraPosition, box.Min, box.Max);

            return Vector3.DistanceSquared(cameraPosition, nearest) <= MaxDrawDistanceSquared;
        }

        /// <summary>
        /// Set by the drawing layer watching this system, or null when nothing draws it.
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
        /// The group this child belongs to, matched by <see cref="ChooseRandomChildrenInGroup"/> on the parent.
        /// </summary>
        private readonly int groupId;

        /// <summary>How long after its parent this child starts (<c>m_flDelay</c>).</summary>
        private float startDelay;

        /// <summary>This child's index in its parent's <c>m_Children</c>, counting the entries that were skipped.</summary>
        private int childSlot;

        /// <summary>How many entries <c>m_Children</c> lists, including those that were skipped.</summary>
        private int childSlotCount;

        /// <summary>The seed every <see cref="Replay"/> starts over from, or null to run on.</summary>
        private int? pinnedSeed;

        /// <summary>
        /// Whether this child exists only for the parent's endcap (<c>m_bEndCap</c>). It waits at
        /// <see cref="float.MinValue"/> until the parent's endcap starts it.
        /// </summary>
        internal bool IsEndCapChild { get; private set; }

        /// <summary>
        /// Whether a model with no hitboxes in the asked set reads as one box around its render bounds,
        /// and a control point without a model as one empty box at the world origin
        /// (<c>m_bShouldHitboxesFallbackToRenderBounds</c>).
        /// </summary>
        internal bool HitboxesFallBackToRenderBounds { get; }

        /// <summary>The lowest detail tier this child appears at (<c>m_nDetailLevel</c>).</summary>
        private ParticleDetailLevel detailLevel;

        private readonly int initialParticles;
        private readonly int maxParticles;
        private readonly float minimumTimeStep;
        private readonly float maximumTimeStep;

        /// <summary>The length of the step being simulated.</summary>
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
        /// Cache a reference to <see cref="AddParticle"/> as to not allocate one for every emitted particle.
        /// </summary>
        private readonly Action<float> addParticleAction;

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

        /// <summary>Resolves the resources the definition and its functions name.</summary>
        internal IFileLoader FileLoader => fileLoader;

        /// <summary>Whether the first frame, which runs the pre-emission operators once and the initial burst, is still to come.</summary>
        private bool firstFramePending = true;

        /// <summary>Whether the control points hold a previous state to interpolate from.</summary>
        private bool previousControlPointsValid;

        /// <summary>
        /// Whether <see cref="IsFinished"/> may pass before the system's clock reaches zero, set for
        /// children a random child selection parks.
        /// </summary>
        private bool finishedBeforeStartAllowed;

        /// <summary>How many frames ran while <c>m_flMaximumSimTime</c> could still cut them.</summary>
        private int preSimFrames;

        /// <summary>Nesting depth of fast-forwards in progress, during which the observer is not told about steps.</summary>
        private int skipDepth;

        /// <summary>The parent particle events any child of this system emits from.</summary>
        private ParticleEventType eventListenMask;

        /// <inheritdoc cref="eventListenMask"/>
        internal ParticleEventType ListenedEvents => eventListenMask;

        private readonly ParticleCollection particleCollection;
        private int particlesEmitted;

        /// <summary>Where the next particle id is drawn from, before the seed displaces it.</summary>
        private int particleIdCounter;
        private ParticleSystemState systemState;

        /// <summary>Builds a runnable system from its definition.</summary>
        /// <param name="particleSystem">The system definition, read through its upgraded tree.</param>
        /// <param name="fileLoader">Resolves the child systems and snapshots the definition names.</param>
        /// <param name="logger">Receives warnings about unimplemented classes and fields. Omit to run silently.</param>
        /// <param name="particleSnapshot">A snapshot to publish to the system, when it runs off one.</param>
        /// <param name="parentSystemState">The state of the system running this one as a child.</param>
        public ParticleSystemSimulation(ParticleSystem particleSystem, IFileLoader fileLoader, ILogger? logger = null, ParticleSnapshot? particleSnapshot = null, ParticleSystemState? parentSystemState = null)
        {
            addParticleAction = AddParticle;

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
            initialParticles = Math.Max(0, parse.Int32("m_nInitialParticles", 0));

            // The pool is capped whatever the file asks for; GPU simulated systems get a far larger one.
            var particleCeiling = parse.Boolean("m_bIsGPUParticleSystem", false) ? ParticleCollection.MAX_GPU_PARTICLES : ParticleCollection.MAX_PARTICLES;
            maxParticles = Math.Clamp(parse.Int32("m_nMaxParticles", 1000), 1, particleCeiling);
            minimumTimeStep = parse.Float("m_flMinimumTimeStep", 0f);
            var authoredMaximumTimeStep = parse.Float("m_flMaximumTimeStep", 0.1f);
            maximumTimeStep = authoredMaximumTimeStep > 0f ? authoredMaximumTimeStep : 0.1f;
            minimumSimTime = parse.Float("m_flMinimumSimTime", 0f);
            maximumSimTime = parse.Float("m_flMaximumSimTime", 0f);
            minimumFrames = parse.Int32("m_nMinimumFrames", 0);
            preSimulationTime = parse.Float("m_flPreSimulationTime", 0f);
            stopSimulationAfterTime = parse.Float("m_flStopSimulationAfterTime", 1e9f);
            HitboxesFallBackToRenderBounds = parse.Boolean("m_bShouldHitboxesFallbackToRenderBounds", true);

            // Non-positive distances, and ones large enough to overflow when squared, draw at any range.
            var maxDrawDistance = parse.Float("m_flMaxDrawDistance", -1f);
            MaxDrawDistanceSquared = maxDrawDistance is <= 0f or >= 1e10f
                ? float.MaxValue
                : maxDrawDistance * maxDrawDistance;

            // A negative time keeps the system awake for practically ever
            var noDrawTimeToGoToSleep = parse.Float("m_flNoDrawTimeToGoToSleep", 8f);
            NoDrawTimeToGoToSleep = noDrawTimeToGoToSleep < 0f ? 65536f : noDrawTimeToGoToSleep;

            currentFrameTime = maximumTimeStep;

            infiniteBounds = parse.Boolean("m_bInfiniteBounds", false);
            particleBoundingBox = new AABB(
                parse.Vector3("m_BoundingBoxMin", new Vector3(-10)),
                parse.Vector3("m_BoundingBoxMax", new Vector3(10))
            );

            var constantAttributes = Particle.CreateConstants(parse);
            particleCollection = new ParticleCollection(constantAttributes, maxParticles);

            systemState = new ParticleSystemState(parentSystemState)
            {
                Data = this,
            };

            Name = particleSystem.Resource?.FileName ?? "<unnamed>";

            IReadOnlyList<KVObject> Functions(string key) => rootData.GetArray(key) ?? [];

            SetupFunctions(Functions("m_PreEmissionOperators"), ParticleControllerFactory.TryCreatePreEmissionOperator, preEmissionOperators, "pre-emission operator");
            SetupFunctions(Functions("m_Emitters"), ParticleControllerFactory.TryCreateEmitter, emitters, "emitter");
            SetupFunctions(Functions("m_Initializers"), ParticleControllerFactory.TryCreateInitializer, initializers, "initializer");
            SetupFunctions(Functions("m_Operators"), ParticleControllerFactory.TryCreateOperator, operators, "operator");
            SetupFunctions(Functions("m_ForceGenerators"), ParticleControllerFactory.TryCreateForceGenerator, ForceGenerators, "force generator");
            SetupFunctions(Functions("m_Constraints"), ParticleControllerFactory.TryCreateConstraint, constraints, "constraint");

            SetupChildParticles(Functions("m_Children"));

            NamedValues = new ParticleNamedValues(rootData, childSimulations.ConvertAll(static child => child.NamedValues));

            PublishSnapshot(parse, particleSnapshot);

            particleCollection.PreserveOrderOnKill = IsOrderImportant(Functions("m_Renderers"));

            foreach (var childSimulation in childSimulations)
            {
                foreach (var emitter in childSimulation.emitters)
                {
                    eventListenMask |= emitter.EventTypeMask;
                }
            }

            if (parentSystemState == null)
            {
                SeedRandom(unchecked((int)StringToken.Get(Name)));
            }
        }

        /// <summary>Builds the running state of every function in the tree, children before their parent.</summary>
        private void InitializeTree()
        {
            foreach (var childSimulation in childSimulations)
            {
                childSimulation.InitializeTree();
            }

            ResetFunctions();
            ApplyNamedValueLocals();
        }

        /// <summary>The system's named values.</summary>
        internal ParticleNamedValues NamedValues { get; }

        /// <summary>Sets the default of every <c>m_NamedValueLocals</c> entry that has one, here and in every system below.</summary>
        private void ApplyNamedValueLocals()
        {
            foreach (var (name, value) in NamedValues.Locals)
            {
                SetNamedValue(name, value);
            }
        }

        /// <summary>
        /// Sets a float named value on this system and every system below it. The value is read by
        /// the next simulation step; after each step the system goes back to the value it held the
        /// step before, so a value meant to hold is set again before every update.
        /// </summary>
        /// <param name="name">The named value, as the definition's <c>m_NamedValueLocals</c> or its inputs name it.</param>
        /// <param name="value">The value.</param>
        /// <returns>Whether this system declares the name with a type the value converts to.</returns>
        public bool SetNamedValue(string name, float value) => SetNamedValue(name, ParticleNamedValue.FromFloat(value));

        /// <inheritdoc cref="SetNamedValue(string, float)"/>
        /// <remarks>A transform named value takes the vector as its position, with no rotation.</remarks>
        public bool SetNamedValue(string name, Vector3 value) => SetNamedValue(name, ParticleNamedValue.FromVector(value));

        /// <inheritdoc cref="SetNamedValue(string, float)"/>
        /// <param name="name">The named value, as the definition's <c>m_NamedValueLocals</c> or its inputs name it.</param>
        /// <param name="position">The transform's position.</param>
        /// <param name="rotation">The transform's rotation. A vector named value takes only the position.</param>
        public bool SetNamedValue(string name, Vector3 position, Quaternion rotation)
            => SetNamedValue(name, ParticleNamedValue.FromTransform(position, rotation));

        private bool SetNamedValue(string name, ParticleNamedValue value)
        {
            var result = NamedValues.Set(name, value);

            foreach (var childSimulation in childSimulations)
            {
                childSimulation.SetNamedValue(name, value);
            }

            return result;
        }

        /// <summary>
        /// The live particle states this frame. Read by a child particle system's
        /// <c>C_INIT_CreateFromParentParticles</c> to seed new particles from this system's current positions/velocities.
        /// </summary>
        internal ParticleSpan CurrentParticles => particleCollection.Current;

        /// <summary>The most particles this system can hold at once.</summary>
        internal int ParticleCapacity => particleCollection.Capacity;

        /// <summary>How many particles the last kill removed from this system.</summary>
        internal int KilledLastPass => particleCollection.KilledLastPass;

        /// <summary>
        /// Sets the particle detail tier for this system; child systems inherit it.
        /// </summary>
        public void SetDetailLevel(ParticleDetailLevel level)
        {
            systemState.DetailLevel = level;
        }

        /// <summary>
        /// Seeds this system and its children, starts their draws over and rebuilds every function's
        /// state from it. As the engine does with a given seed, every entry of <c>m_Children</c> steps
        /// the seed by 129, including entries that are skipped, so the child in slot n takes the seed
        /// 129 * (n + 1) above it and this system the step after the last slot. The engine seeds a new
        /// system from the clock; here it is seeded from its name, so instances of one effect play out
        /// alike unless each is seeded apart, such as from where it is placed.
        /// </summary>
        /// <param name="seed">Any value, of which the low 12 bits are used.</param>
        internal void SeedRandom(int seed)
        {
            ApplySeed(seed, 0);
            InitializeTree();
        }

        /// <summary>
        /// Fixes the seed this system and its children start over from each time they replay, so every
        /// replay plays out the same, with the children's seeds stepped as <see cref="SeedRandom"/> does.
        /// Each replay also puts the control points that functions wrote back to how the host left them.
        /// Takes effect at the next replay.
        /// </summary>
        /// <param name="seed">The seed, or null to let each replay run on from the last as the engine does.</param>
        internal void PinRandomSeed(int? seed) => pinnedSeed = seed;

        /// <summary>
        /// Seeds this system and its children as <see cref="SeedRandom"/> describes and starts their draws
        /// and particle ids over. A child's draws start past those of the children listed before it.
        /// </summary>
        private void ApplySeed(int seed, int counter)
        {
            for (var i = 0; i < childSimulations.Count; i++)
            {
                var child = childSimulations[i];
                child.ApplySeed(ChildSeed(seed, child.childSlot), (i + 1) * child.maxParticles);
            }

            systemState.Random.Start(ChildSeed(seed, childSlotCount - 1), counter);
            particleIdCounter = 0;
        }

        /// <summary>The seed of the <c>m_Children</c> entry in <paramref name="slot"/>, stepped from its parent's.</summary>
        private static int ChildSeed(int seed, int slot) => unchecked(seed + (129 * (slot + 1)));

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

            // Spawning moves emission-order state that real spawns rely on for determinism, so
            // EndPrewarm puts it back and these leave no trace once the real simulation starts.
            var scope = new PrewarmScope(particlesEmitted, systemState.ParticleCount) { ParticleIdCounter = particleIdCounter };

            for (var i = 0; i < count; i++)
            {
                AddParticle(systemState.Age);
            }

            InitializeNewParticles(0, particleCollection.Count, 0, -1, raiseEvents: false);

            return scope;
        }

        /// <summary>Drops the particles <see cref="BeginPrewarm"/> spawned and restores emission state.</summary>
        /// <param name="scope">What <see cref="BeginPrewarm"/> returned.</param>
        public void EndPrewarm(PrewarmScope scope)
        {
            particleCollection.Clear();
            particlesEmitted = scope.ParticlesEmitted;
            particleIdCounter = scope.ParticleIdCounter;
            systemState.ParticleCount = scope.ParticleCount;
        }

        /// <summary>The emission state a prewarm disturbed, handed back to <see cref="EndPrewarm"/>.</summary>
        /// <param name="ParticlesEmitted">Emission counter before the prewarm.</param>
        /// <param name="ParticleCount">Particle count before the prewarm.</param>
        public readonly record struct PrewarmScope(int ParticlesEmitted, long ParticleCount)
        {
            /// <summary>Particle id counter before the prewarm.</summary>
            internal int ParticleIdCounter { get; init; }
        }

        /// <summary>
        /// Gets how far to blend from <see cref="ParticleCollection.PreviousStep"/> toward
        /// <see cref="ParticleCollection.Current"/> to draw the system at the time it was asked to reach.
        /// A minimum time step can carry the simulation past that time, and the blend draws it where
        /// it would have been. Particles created after <see cref="ParticleSystemState.RequestedAge"/> do
        /// not exist yet at that time and should not be drawn.
        /// </summary>
        /// <param name="fraction">0 for the previous step, 1 for the current one.</param>
        /// <returns>Whether the last step overshot and the blend applies.</returns>
        public bool TryGetRenderBlend(out float fraction)
        {
            var stepStart = systemState.StepStartAge;
            var requested = systemState.RequestedAge;
            var time = systemState.Age;

            if (systemState.Frozen || particleCollection.PreviousStep.IsEmpty || !(time > requested && requested >= stepStart))
            {
                fraction = 1f;
                return false;
            }

            fraction = (requested - stepStart) / (time - stepStart);
            return true;
        }

        /// <summary>Runs the observer updates and bounds a fast-forward skipped, children first.</summary>
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
            var listIndex = 0;

            foreach (var info in data)
            {
                var className = info.GetStringProperty("_class");
                var disabled = IsOperatorDisabled(info, logger);

                if (disabled || !IsInVersionRange(className))
                {
                    if (disabled && BehaviorVersion < 5)
                    {
                        listIndex++;
                    }

                    definitionIndex++;
                    continue;
                }

                if (tryCreate(className, info, logger, BehaviorVersion, out var function))
                {
                    if (function is ParticleFunctionInitializer initializer)
                    {
                        initializer.DefinitionIndex = definitionIndex;
                    }
                    else if (function is ParticleFunctionEmitter emitter)
                    {
                        emitter.EmitterIndex = listIndex;
                    }

                    target.Add(function);
                    listIndex++;
                }
                else
                {
                    logger.LogUniqueWarningFor([label, className], UnsupportedClassWarning, label, className, Name);
                }

                definitionIndex++;
            }
        }

        private bool IsInVersionRange(string className)
        {
            foreach (var (name, minVersion, maxVersion) in ClassVersionRanges)
            {
                if (name == className)
                {
                    return BehaviorVersion >= minVersion && BehaviorVersion <= maxVersion;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds the control point defaults of every function of this system and its children. Renderers
        /// live with the renderer and add theirs there.
        /// </summary>
        internal void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            ParticleFunction[] functions = [.. preEmissionOperators, .. emitters, .. initializers, .. operators, .. ForceGenerators, .. constraints];

            foreach (var function in functions)
            {
                function.AddControlPointDefaults(defaults);
            }

            foreach (var child in childSimulations)
            {
                child.AddControlPointDefaults(defaults);
            }
        }

        /// <summary>
        /// Whether a function or renderer of this system, or any child, needs particle order kept when
        /// particles are removed.
        /// </summary>
        private bool IsOrderImportant(IReadOnlyList<KVObject> rendererData)
        {
            if (operators.Exists(static op => op.IsOrderImportant) || childSimulations.Exists(static child => child.particleCollection.PreserveOrderOnKill))
            {
                return true;
            }

            foreach (var renderer in rendererData)
            {
                if (IsOperatorDisabled(renderer, logger))
                {
                    continue;
                }

                var className = renderer.GetStringProperty("_class");

                if (className == "C_OP_RenderRopes"
                    || (className == "C_OP_RenderProjected" && renderer.GetArray("m_MaterialVars") is { Count: > 0 }))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOperatorDisabled(KVObject op, ILogger logger)
        {
            var parse = new ParticleDefinitionParser(op, logger);

            return parse.Boolean("m_bDisableOperator", default);
        }
    }
}
