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
    internal partial class ParticleRenderer
    {
        private readonly List<ParticleFunctionPreEmissionOperator> preEmissionOperators = [];
        private readonly List<ParticleFunctionEmitter> emitters = [];

        private readonly List<ParticleFunctionInitializer> initializers = [];

        private readonly List<ParticleFunctionOperator> operators = [];

        // Run by C_OP_BasicMovement (not the operator loop): each of its instances asks every force
        // generator to add accelerations into Particle.ForceAccumulator, then integrates and clears it.
        internal readonly List<ParticleFunctionForceGenerator> ForceGenerators = [];

        private readonly List<ParticleFunctionConstraint> constraints = [];

        private readonly List<ParticleFunctionRenderer> renderers = [];

        // Caps pre-simulation substeps for pathological content; the largest shipped effect needs 1500
        // (15s at 0.01 step).
        private const int MaxPreSimulationSteps = 2048;

        // Upper bound on constraint work-list rounds per frame (m_nMaxConstraintPasses, default 3).
        // A lone constraint settles in one round; the bound only matters when multiple constraints
        // invalidate each other. ReadConstraintPasses returns 1 for systems with no constraints.
        private readonly int constraintPasses;

        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        private readonly Scene scene;

        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        /// <summary>
        /// The passes this system draws in, unioned over its renderers and its children's. Fixed once the
        /// system is built.
        /// </summary>
        public CustomRenderPasses Passes { get; private set; }

        /// <summary>
        /// The scene node this system renders under, when created for one. renderers use its
        /// per-node lighting bindings.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        public string Name { get; set; }
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

        public ControlPoint MainControlPoint
        {
            get => GetControlPoint(0);
            set => systemRenderState.SetControlPoint(0, value);
        }

        /// <summary>
        /// Publishes the render camera position to this system and every child for
        /// camera-dependent particle inputs.
        /// </summary>
        public void SetCameraPosition(Vector3 position)
        {
            systemRenderState.CameraPosition = position;

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.SetCameraPosition(position);
            }
        }

        public ControlPoint GetControlPoint(int cp) => systemRenderState.GetControlPoint(cp);

        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly RendererContext rendererContext;
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
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, RendererContext rendererContext, Scene scene, ParticleSnapshot? particleSnapshot = null, ParticleSystemRenderState? parentSystemRenderState = null)
        {
            emitParticleAction = EmitParticle;

            childParticleRenderers = [];
            this.rendererContext = rendererContext;
            this.scene = scene;

            var rootData = particleSystem.GetUpgradedData();
            var parse = new ParticleDefinitionParser(rootData, rendererContext.Logger);
            BehaviorVersion = parse.Int32("m_nBehaviorVersion", 0);
            parse = parse with { BehaviorVersion = BehaviorVersion };
            firstMultipleOverride = parse.Int32("m_nFirstMultipleOverride_BackwardCompat", -1);
            groupId = parse.Int32("m_nGroupID", 0);
            initialParticles = parse.Int32("m_nInitialParticles", 0);
            maxParticles = parse.Int32("m_nMaxParticles", 1000);
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

            systemRenderState = new ParticleSystemRenderState(parentSystemRenderState)
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

            SetupRenderers(Functions("m_Renderers"));

            SetupFunctions(Functions("m_PreEmissionOperators"), ParticleControllerFactory.TryCreatePreEmissionOperator, preEmissionOperators, "pre-emission operator");

            SetupChildParticles(Functions("m_Children"));

            Passes = CollectPasses();

            CalculateBounds();
        }

        private CustomRenderPasses CollectPasses()
        {
            var passes = CustomRenderPasses.None;

            foreach (var renderer in renderers)
            {
                passes |= renderer.Pass == RenderPass.Opaque
                    ? CustomRenderPasses.Opaque
                    : CustomRenderPasses.Translucent;
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                passes |= childParticleRenderer.Passes;
            }

            return passes;
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
            systemRenderState.DetailLevel = level;
        }


        /// <summary>
        /// Draws the renderers belonging to <paramref name="pass"/>.
        /// </summary>
        public void Render(Camera camera, RenderPass pass)
        {
            var wantedPass = pass == RenderPass.DepthOnly ? RenderPass.Opaque : pass;

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                if (!childParticleRenderer.ChildShouldRun(systemRenderState))
                {
                    continue;
                }

                childParticleRenderer.Render(camera, pass);
            }

            if (particleCollection.Count > 0)
            {
                var rendered = false;

                foreach (var renderer in renderers)
                {
                    if (renderer.Pass != wantedPass)
                    {
                        continue;
                    }

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
                if (!childParticleRenderer.ChildShouldRun(systemRenderState))
                {
                    continue;
                }

                childParticleRenderer.Prewarm(camera);
            }

            if (renderers.Count == 0 || particleCollection.Count > 0)
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

            foreach (var renderer in renderers)
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
            => renderers
                .SelectMany(static renderer => renderer.GetSupportedRenderModes())
                .Concat(childParticleRenderers.SelectMany(static child => child.GetSupportedRenderModes()));

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in renderers)
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

            foreach (var renderer in renderers)
            {
                renderer.Update(particleCollection, systemRenderState);
            }

            CalculateBounds();
        }


        private delegate bool TryCreateFunction<T>(string className, KVObject data, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out T result);

        private void SetupFunctions<T>(IEnumerable<KVObject> data, TryCreateFunction<T> tryCreate, List<T> target, string label)
        {
            var definitionIndex = 0;

            foreach (var info in data)
            {
                if (IsOperatorDisabled(info, rendererContext.Logger))
                {
                    definitionIndex++;
                    continue;
                }

                var className = info.GetStringProperty("_class");
                if (tryCreate(className, info, rendererContext.Logger, BehaviorVersion, out var function))
                {
                    if (function is ParticleFunctionInitializer initializer)
                    {
                        initializer.DefinitionIndex = definitionIndex;
                    }

                    target.Add(function);
                }
                else
                {
                    rendererContext.Logger.LogUniqueWarningFor([label, className], UnsupportedClassWarning, label, className, Name);
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
                if (op.GetStringProperty("_class") == "C_OP_BasicMovement" && !IsOperatorDisabled(op, rendererContext.Logger))
                {
                    var parse = new ParticleDefinitionParser(op, rendererContext.Logger, BehaviorVersion);
                    passes = Math.Max(passes, parse.Int32("m_nMaxConstraintPasses", 3));
                }
            }

            return passes;
        }

        private void SetupRenderers(IEnumerable<KVObject> rendererData)
        {
            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo, rendererContext.Logger))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetStringProperty("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, rendererContext, scene, BehaviorVersion, out var renderer))
                {
                    renderers.Add(renderer);
                }
                else
                {
                    rendererContext.Logger.LogUniqueWarningFor(["renderer", rendererClass], UnsupportedClassWarning, "renderer", rendererClass, Name);
                }
            }
        }


        private static bool IsOperatorDisabled(KVObject op, ILogger logger)
        {
            var parse = new ParticleDefinitionParser(op, logger);

            return parse.Boolean("m_bDisableOperator", default);
        }

        /// <summary>
        /// Replaces the texture every renderer in this system and its children draws with.
        /// </summary>
        public void SetTextureOverride(string textureName)
            => SetTextureOverride(rendererContext.MaterialLoader.GetTexture(textureName, srgbRead: true));

        public void SetTextureOverride(RenderTexture texture)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetTextureOverride(texture);
            }

            foreach (var childRenderer in childParticleRenderers)
            {
                childRenderer.SetTextureOverride(texture);
            }
        }

        // todo: set this when viewer checkbox is toggled
        public void SetWireframe(bool isWireframe)
        {
            foreach (var renderer in renderers)
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
            foreach (var renderer in renderers)
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
