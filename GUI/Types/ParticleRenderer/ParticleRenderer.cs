using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleRenderer
    {
        public IEnumerable<IParticleEmitter> Emitters { get; private set; } = new List<IParticleEmitter>();

        public IEnumerable<IParticleInitializer> Initializers { get; private set; } = new List<IParticleInitializer>();

        public IEnumerable<IParticleOperator> Operators { get; private set; } = new List<IParticleOperator>();

        public IEnumerable<IParticleRenderer> Renderers { get; private set; } = new List<IParticleRenderer>();

        private readonly ParticleSystem particleSystem;
        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly VrfGuiContext vrfGuiContext;

        private List<Particle> particles;
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, VrfGuiContext vrfGuiContext)
        {
            this.particleSystem = particleSystem;
            childParticleRenderers = new List<ParticleRenderer>();
            this.vrfGuiContext = vrfGuiContext;

            particles = new List<Particle>();
            systemRenderState = new ParticleSystemRenderState();

            SetupEmitters(particleSystem.GetData(), particleSystem.GetEmitters());
            SetupInitializers(particleSystem.GetInitializers());
            SetupOperators(particleSystem.GetOperators());
            SetupRenderers(particleSystem.GetRenderers());

            SetupChildParticles(particleSystem.GetChildParticleNames(true));

            Start();
        }

        public void Start()
        {
            foreach (var emitter in Emitters)
            {
                emitter.Start(OnParticleSpawn);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Start();
            }
        }

        private void OnParticleSpawn(Particle particle)
        {
            foreach (var initializer in Initializers)
            {
                initializer.Initialize(particle, systemRenderState);
            }

            particles.Add(particle);
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
            systemRenderState.Lifetime = 0;
            particles.Clear();
            Start();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }
        }

        public void Update(float frameTime)
        {
            systemRenderState.Lifetime += frameTime;

            foreach (var emitter in Emitters)
            {
                emitter.Update(frameTime);
            }

            foreach (var particleOperator in Operators)
            {
                particleOperator.Update(particles, frameTime, systemRenderState);
            }

            // Remove all dead particles
            particles = particles.Where(p => p.Lifetime > 0).ToList();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Update(frameTime);
            }

            // Restart if all emitters are done and all particles expired
            if (Emitters.All(e => e.IsFinished) && particles.Count == 0)
            {
                Restart();
            }
        }

        public void Render(Camera camera)
        {
            foreach (var renderer in Renderers)
            {
                renderer.Render(particles, camera.ProjectionMatrix, camera.CameraViewMatrix);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Render(camera);
            }
        }

        private void SetupEmitters(IKeyValueCollection baseProperties, IEnumerable<IKeyValueCollection> emitterData)
        {
            var emitters = new List<IParticleEmitter>();

            foreach (var emitterInfo in emitterData)
            {
                var emitterClass = emitterInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateEmitter(emitterClass, baseProperties, emitterInfo, out var emitter))
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
            var initializers = new List<IParticleInitializer>();

            foreach (var initializerInfo in initializerData)
            {
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
            var operators = new List<IParticleOperator>();

            foreach (var operatorInfo in operatorData)
            {
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
            var renderers = new List<IParticleRenderer>();

            foreach (var rendererInfo in rendererData)
            {
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

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = FileExtensions.LoadFileByAnyMeansNecessary(childName + "_c", vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);
                var childSystem = new ParticleSystem(childResource);

                childParticleRenderers.Add(new ParticleRenderer(childSystem, vrfGuiContext));
            }
        }
    }
}
