using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.PreEmissionOperators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleRenderer : IRenderer
    {
        public IEnumerable<ParticleFunctionPreEmissionOperator> PreEmissionOperators { get; private set; } = new List<ParticleFunctionPreEmissionOperator>();
        public IEnumerable<ParticleFunctionEmitter> Emitters { get; private set; } = new List<ParticleFunctionEmitter>();

        public IEnumerable<ParticleFunctionInitializer> Initializers { get; private set; } = new List<ParticleFunctionInitializer>();

        public IEnumerable<ParticleFunctionOperator> Operators { get; private set; } = new List<ParticleFunctionOperator>();

        public IEnumerable<ParticleFunctionRenderer> Renderers { get; private set; } = new List<ParticleFunctionRenderer>();

        public AABB LocalBoundingBox { get; private set; } = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

        public int BehaviorVersion { get; private set; }

        public int InitialParticles { get; init; }
        public int MaxParticles { get; init; }

        /// <summary>
        /// The particle bounds to use when calculating the bounding box of the particle system.
        /// This is added over the particle's radius value.
        /// </summary>
        private AABB ParticleBoundingBox { get; init; }

        /// <summary>
        /// Set to true to never cull this particle system.
        /// </summary>
        private bool InfiniteBounds { get; init; }


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
            childParticleRenderers = new List<ParticleRenderer>();
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
                emitter.Start(EmitParticle);
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
            => Emitters.All(e => e.IsFinished)
            && particleCollection.Count == 0
            && childParticleRenderers.All(r => r.IsFinished());

        public void Render(Camera camera, RenderPass renderPass)
        {
            if (renderPass == RenderPass.Translucent || renderPass == RenderPass.Both)
            {
                foreach (var childParticleRenderer in childParticleRenderers)
                {
                    childParticleRenderer.Render(camera, renderPass);
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

        private void SetupEmitters(IEnumerable<IKeyValueCollection> emitterData)
        {
            var emitters = new List<ParticleFunctionEmitter>();

            foreach (var emitterInfo in emitterData)
            {
                if (IsOperatorDisabled(emitterInfo))
                {
                    continue;
                }

                var emitterClass = emitterInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateEmitter(emitterClass, emitterInfo, out var emitter))
                {
                    emitters.Add(emitter);
                }
                else
                {
                    Console.WriteLine($"Unsupported emitter class '{emitterClass}'.");
                }
            }

            Emitters = emitters;
        }

        private void SetupInitializers(IEnumerable<IKeyValueCollection> initializerData)
        {
            var initializers = new List<ParticleFunctionInitializer>();

            foreach (var initializerInfo in initializerData)
            {
                if (IsOperatorDisabled(initializerInfo))
                {
                    continue;
                }

                var initializerClass = initializerInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateInitializer(initializerClass, initializerInfo, out var initializer))
                {
                    initializers.Add(initializer);
                }
                else
                {
                    Console.WriteLine($"Unsupported initializer class '{initializerClass}'.");
                }
            }

            Initializers = initializers;
        }

        private void SetupOperators(IEnumerable<IKeyValueCollection> operatorData)
        {
            var operators = new List<ParticleFunctionOperator>();

            foreach (var operatorInfo in operatorData)
            {
                if (IsOperatorDisabled(operatorInfo))
                {
                    continue;
                }

                var operatorClass = operatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateOperator(operatorClass, operatorInfo, out var @operator))
                {
                    operators.Add(@operator);
                }
                else
                {
                    Console.WriteLine($"Unsupported operator class '{operatorClass}'.");
                }
            }

            Operators = operators;
        }

        private void SetupRenderers(IEnumerable<IKeyValueCollection> rendererData)
        {
            var renderers = new List<ParticleFunctionRenderer>();

            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, vrfGuiContext, out var renderer))
                {
                    renderers.Add(renderer);
                }
                else
                {
                    Console.WriteLine($"Unsupported renderer class '{rendererClass}'.");
                }
            }

            Renderers = renderers;
        }
        private void SetupPreEmissionOperators(IEnumerable<IKeyValueCollection> preEmissionOperatorData)
        {
            var preEmissionOperators = new List<ParticleFunctionPreEmissionOperator>();

            foreach (var preEmissionOperatorInfo in preEmissionOperatorData)
            {
                if (IsOperatorDisabled(preEmissionOperatorInfo))
                {
                    continue;
                }

                var preEmissionOperatorClass = preEmissionOperatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreatePreEmissionOperator(preEmissionOperatorClass, preEmissionOperatorInfo, out var preEmissionOperator))
                {
                    preEmissionOperators.Add(preEmissionOperator);
                }
                else
                {
                    Console.WriteLine($"Unsupported pre-emission operator class '{preEmissionOperatorClass}'.");
                }
            }

            PreEmissionOperators = preEmissionOperators;
        }

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = vrfGuiContext.LoadFileByAnyMeansNecessary(childName + "_c");

                if (childResource == null)
                {
                    continue;
                }

                var childSystemDefinition = (ParticleSystem)childResource.DataBlock;
                var childSystem = new ParticleRenderer(childSystemDefinition, vrfGuiContext);
                childSystem.MainControlPoint = MainControlPoint;

                childParticleRenderers.Add(childSystem);
            }
        }

        private static bool IsOperatorDisabled(IKeyValueCollection op)
        {
            var parse = new ParticleDefinitionParser(op);

            // Also skip ops that only run during endcap (currently unsupported)
            return parse.Boolean("m_bDisableOperator", default)
                || parse.Enum<ParticleEndCapMode>("m_nOpEndCapState", default) == ParticleEndCapMode.PARTICLE_ENDCAP_ENDCAP_ON;
        }

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
