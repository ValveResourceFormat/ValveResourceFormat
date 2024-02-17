using System.Linq;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.PreEmissionOperators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
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
        private readonly VrfGuiContext vrfGuiContext;
        private bool hasStarted;

        private readonly ParticleCollection particleCollection;
        private int particlesEmitted;
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, VrfGuiContext vrfGuiContext)
        {
            emitParticleAction = EmitParticle;

            childParticleRenderers = [];
            this.vrfGuiContext = vrfGuiContext;

            var parse = new ParticleDefinitionParser(particleSystem.Data);
            BehaviorVersion = parse.Int32("m_nBehaviorVersion", 13);
            InitialParticles = parse.Int32("m_nInitialParticles", 0);
            MaxParticles = parse.Int32("m_nMaxParticles", 1000);

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
            particleCollection.Initial[index].Position = MainControlPoint.Position;

            foreach (var initializer in Initializers)
            {
                initializer.Initialize(ref particleCollection.Initial[index], systemRenderState);
            }

            particleCollection.Current[index] = particleCollection.Initial[index];
            particleCollection.Current[index].ParticleID = particlesEmitted++;
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
                .SelectMany(renderer => renderer.GetSupportedRenderModes())
                .Concat(childParticleRenderers.SelectMany(child => child.GetSupportedRenderModes()))
                .Distinct();

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
                if (IsOperatorDisabled(emitterInfo))
                {
                    continue;
                }

                var emitterClass = emitterInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateEmitter(emitterClass, emitterInfo, out var emitter))
                {
                    Emitters.Add(emitter);
                }
                else
                {
                    Log.Warn(nameof(ParticleRenderer), $"Unsupported emitter class '{emitterClass}'.");
                }
            }
        }

        private void SetupInitializers(IEnumerable<KVObject> initializerData)
        {
            foreach (var initializerInfo in initializerData)
            {
                if (IsOperatorDisabled(initializerInfo))
                {
                    continue;
                }

                var initializerClass = initializerInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateInitializer(initializerClass, initializerInfo, out var initializer))
                {
                    Initializers.Add(initializer);
                }
                else
                {
                    Log.Warn(nameof(ParticleRenderer), $"Unsupported initializer class '{initializerClass}'.");
                }
            }
        }

        private void SetupOperators(IEnumerable<KVObject> operatorData)
        {
            foreach (var operatorInfo in operatorData)
            {
                if (IsOperatorDisabled(operatorInfo))
                {
                    continue;
                }

                var operatorClass = operatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateOperator(operatorClass, operatorInfo, out var @operator))
                {
                    Operators.Add(@operator);
                }
                else
                {
                    Log.Warn(nameof(ParticleRenderer), $"Unsupported operator class '{operatorClass}'.");
                }
            }
        }

        private void SetupRenderers(IEnumerable<KVObject> rendererData)
        {
            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, vrfGuiContext, out var renderer))
                {
                    Renderers.Add(renderer);
                }
                else
                {
                    Log.Warn(nameof(ParticleRenderer), $"Unsupported renderer class '{rendererClass}'.");
                }
            }
        }
        private void SetupPreEmissionOperators(IEnumerable<KVObject> preEmissionOperatorData)
        {
            foreach (var preEmissionOperatorInfo in preEmissionOperatorData)
            {
                if (IsOperatorDisabled(preEmissionOperatorInfo))
                {
                    continue;
                }

                var preEmissionOperatorClass = preEmissionOperatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreatePreEmissionOperator(preEmissionOperatorClass, preEmissionOperatorInfo, out var preEmissionOperator))
                {
                    PreEmissionOperators.Add(preEmissionOperator);
                }
                else
                {
                    Log.Warn(nameof(ParticleRenderer), $"Unsupported pre-emission operator class '{preEmissionOperatorClass}'.");
                }
            }
        }

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = vrfGuiContext.LoadFileCompiled(childName);

                if (childResource == null)
                {
                    continue;
                }

                var childSystemDefinition = (ParticleSystem)childResource.DataBlock;
                var childSystem = new ParticleRenderer(childSystemDefinition, vrfGuiContext)
                {
                    MainControlPoint = MainControlPoint
                };

                childParticleRenderers.Add(childSystem);
            }
        }

        private static bool IsOperatorDisabled(KVObject op)
        {
            var parse = new ParticleDefinitionParser(op);

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
