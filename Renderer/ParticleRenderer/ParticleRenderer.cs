using System.Diagnostics;
using System.Linq;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.PreEmissionOperators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleRenderer
    {
        private readonly List<ParticleFunctionPreEmissionOperator> PreEmissionOperators = [];
        private readonly List<ParticleFunctionEmitter> Emitters = [];

        private readonly List<ParticleFunctionInitializer> Initializers = [];

        private readonly List<ParticleFunctionOperator> Operators = [];

        private readonly List<ParticleFunctionRenderer> Renderers = [];

        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        public int BehaviorVersion { get; }

        private readonly int InitialParticles;
        private readonly int MaxParticles;
        private readonly float MaximumTimeStep;

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
            get => systemRenderState.GetControlPoint(0);
            set => systemRenderState.SetControlPoint(0, value);
        }

        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly RendererContext RendererContext;
        private bool hasStarted;

        private readonly ParticleCollection particleCollection;
        private int particlesEmitted;
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, RendererContext rendererContext)
        {
            emitParticleAction = EmitParticle;

            childParticleRenderers = [];
            this.RendererContext = rendererContext;

            var parse = new ParticleDefinitionParser(particleSystem.Data, rendererContext.Logger);
            BehaviorVersion = parse.Int32("m_nBehaviorVersion", 13);
            InitialParticles = parse.Int32("m_nInitialParticles", 0);
            MaxParticles = parse.Int32("m_nMaxParticles", 1000);
            MaximumTimeStep = parse.Float("m_flMaximumTimeStep", 0.1f);

            InfiniteBounds = parse.Boolean("m_bInfiniteBounds", false);
            ParticleBoundingBox = new AABB(
                parse.Vector3("m_BoundingBoxMin", new Vector3(-10)),
                parse.Vector3("m_BoundingBoxMax", new Vector3(10))
            );

            var constantAttributes = new Particle(parse);
            particleCollection = new ParticleCollection(constantAttributes, MaxParticles);

            systemRenderState = new ParticleSystemRenderState()
            {
                Data = this,
                EndEarly = false
            };

            SetupEmitters(particleSystem.GetEmitters());
            SetupInitializers(particleSystem.GetInitializers());
            SetupOperators(particleSystem.GetOperators());
            SetupForceGenerators(particleSystem.GetForceGenerators());
            SetupRenderers(particleSystem.GetRenderers());
            SetupPreEmissionOperators(particleSystem.GetPreEmissionOperators());

            SetupChildParticles(particleSystem.GetChildParticleNames(true));

            CalculateBounds();
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

            if (systemRenderState.ParticleCount >= MaxParticles)
            {
                return;
            }

            systemRenderState.ParticleCount += 1;

            // TODO: Make particle positions and control points local space
            particleCollection.Current[index] = particleCollection.Initial[index];

            // Particle id must be set before initializing because the deterministic randomness uses particle ids
            particleCollection.Current[index].ParticleID = particlesEmitted++;
            particleCollection.Current[index].Position = MainControlPoint.Position;

            foreach (var initializer in Initializers)
            {
                initializer.Initialize(ref particleCollection.Current[index], systemRenderState);
            }
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
            particleCollection.Clear();
            Start();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }
        }

        public void Update(float frameTime)
        {
            if (!hasStarted)
            {
                Start();
                hasStarted = true;
            }

            if (frameTime > MaximumTimeStep)
            {
                frameTime = MaximumTimeStep;
            }

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
                emitter.Emit(frameTime);
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

            // Increase age of all particles
            for (var i = 0; i < particleCollection.Count; ++i)
            {
                particleCollection.Current[i].Age += frameTime;
            }

            // Remove all dead particles
            particleCollection.PruneExpired();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Update(frameTime);
            }

            CalculateBounds();

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

                    renderer.Render(particleCollection, systemRenderState, camera.CameraViewMatrix);
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

        private void CalculateBounds()
        {
            if (InfiniteBounds)
            {
                return;
            }

            var newBounds = new AABB();
            var worldCenter = MainControlPoint.Position;
            var additionalBounds = ParticleBoundingBox;

            foreach (ref var particle in particleCollection.Current)
            {
                var pos = particle.Position - worldCenter;
                var radius = new Vector3(particle.Radius);

                newBounds = newBounds.Union(new AABB(pos - radius - additionalBounds.Min, pos + radius + additionalBounds.Max));
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                newBounds = newBounds.Union(childParticleRenderer.LocalBoundingBox);
            }

            LocalBoundingBox = newBounds;
        }

        private void SetupEmitters(IEnumerable<KVObject> emitterData)
        {
            foreach (var emitterInfo in emitterData)
            {
                if (IsOperatorDisabled(emitterInfo, RendererContext.Logger))
                {
                    continue;
                }

                var emitterClass = emitterInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateEmitter(emitterClass, emitterInfo, RendererContext.Logger, out var emitter))
                {
                    Emitters.Add(emitter);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported emitter class '{EmitterClass}'", emitterClass);
                }
            }
        }

        private void SetupInitializers(IEnumerable<KVObject> initializerData)
        {
            foreach (var initializerInfo in initializerData)
            {
                if (IsOperatorDisabled(initializerInfo, RendererContext.Logger))
                {
                    continue;
                }

                var initializerClass = initializerInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateInitializer(initializerClass, initializerInfo, RendererContext.Logger, out var initializer))
                {
                    Initializers.Add(initializer);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported initializer class '{InitializerClass}'", initializerClass);
                }
            }
        }

        private void SetupOperators(IEnumerable<KVObject> operatorData)
        {
            foreach (var operatorInfo in operatorData)
            {
                if (IsOperatorDisabled(operatorInfo, RendererContext.Logger))
                {
                    continue;
                }

                var operatorClass = operatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateOperator(operatorClass, operatorInfo, RendererContext.Logger, out var @operator))
                {
                    Operators.Add(@operator);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported operator class '{OperatorClass}'", operatorClass);
                }
            }
        }

        private void SetupForceGenerators(IEnumerable<KVObject> forceGeneratorData)
        {
            foreach (var forceGenerator in forceGeneratorData)
            {
                if (IsOperatorDisabled(forceGenerator, RendererContext.Logger))
                {
                    continue;
                }

                var operatorClass = forceGenerator.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateForceGenerator(operatorClass, forceGenerator, RendererContext.Logger, out var @operator))
                {
                    Operators.Add(@operator);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported force generator class '{OperatorClass}'", operatorClass);
                }
            }
        }

        private void SetupRenderers(IEnumerable<KVObject> rendererData)
        {
            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo, RendererContext.Logger))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, RendererContext, out var renderer))
                {
                    Renderers.Add(renderer);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported renderer class '{RendererClass}'", rendererClass);
                }
            }
        }
        private void SetupPreEmissionOperators(IEnumerable<KVObject> preEmissionOperatorData)
        {
            foreach (var preEmissionOperatorInfo in preEmissionOperatorData)
            {
                if (IsOperatorDisabled(preEmissionOperatorInfo, RendererContext.Logger))
                {
                    continue;
                }

                var preEmissionOperatorClass = preEmissionOperatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreatePreEmissionOperator(preEmissionOperatorClass, preEmissionOperatorInfo, RendererContext.Logger, out var preEmissionOperator))
                {
                    PreEmissionOperators.Add(preEmissionOperator);
                }
                else
                {
                    RendererContext.Logger.LogWarning("Unsupported pre-emission operator class '{PreEmissionOperatorClass}'", preEmissionOperatorClass);
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

                var childSystem = new ParticleRenderer(childSystemDefinition, RendererContext)
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
    }
}
