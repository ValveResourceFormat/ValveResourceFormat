using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using OpenTK;
using OpenTK.Graphics.OpenGL;
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

        private readonly Camera camera;

        private readonly VrfGuiContext vrfGuiContext;

        private List<Particle> particles;

        private ParticleGrid particleGrid;

        public ParticleRenderer(ParticleSystem particleSystem, GLRenderControl glControl, VrfGuiContext vrfGuiContext)
        {
            this.particleSystem = particleSystem;
            this.vrfGuiContext = vrfGuiContext;

            camera = glControl.Camera;

            particles = new List<Particle>();

            // Initialize after GL loaded
            glControl.Load += (_, __) =>
            {
                GL.Enable(EnableCap.Blend);

                camera.SetViewportSize(glControl.Control.Width, glControl.Control.Height);
                camera.SetLocation(new Vector3(200));
                camera.LookAt(new Vector3(0));

                particleGrid = new ParticleGrid(20, 5);

                SetupEmitters(particleSystem.GetBaseProperties(), particleSystem.GetEmitters());
                SetupInitializers(particleSystem.GetInitializers());
                SetupOperators(particleSystem.GetOperators());
                SetupRenderers(particleSystem.GetRenderers());

                Start();
            };

            glControl.Paint += (_, args) =>
            {
                Update(args.FrameTime); // Couple update to painting for now
                Render();
            };
        }

        public void Start()
        {
            foreach (var emitter in Emitters)
            {
                emitter.Start(OnParticleSpawn);
            }
        }

        private void OnParticleSpawn(Particle particle)
        {
            foreach (var initializer in Initializers)
            {
                initializer.Initialize(particle);
            }

            particles.Add(particle);
        }

        public void Stop()
        {
            foreach (var emitter in Emitters)
            {
                emitter.Stop();
            }
        }

        public void Restart()
        {
            Stop();
            particles.Clear();
            Start();
        }

        public void Update(float frameTime)
        {
            foreach (var particleOperator in Operators)
            {
                particleOperator.Update(particles, frameTime);
            }

            // Remove all dead particles
            particles = particles.Where(p => p.Lifetime > 0).ToList();

            foreach (var emitter in Emitters)
            {
                emitter.Update(frameTime);
            }

            // Restart if all emitters are done and all particles expired
            if (Emitters.All(e => e.IsFinished) && particles.Count == 0)
            {
                Restart();
            }
        }

        public void Render()
        {
            particleGrid?.Render(camera.ProjectionMatrix, camera.CameraViewMatrix);

            foreach (var renderer in Renderers)
            {
                renderer.Render(particles, camera.ProjectionMatrix, camera.CameraViewMatrix);
            }
        }

        private void SetupEmitters(IKeyValueCollection baseProperties, IEnumerable<IKeyValueCollection> emitterData)
        {
            var emitters = new List<IParticleEmitter>();

            foreach (var emitterInfo in emitterData)
            {
                var emitterClass = emitterInfo.GetProperty<string>("_class");
                switch (emitterClass)
                {
                    case "C_OP_InstantaneousEmitter":
                        emitters.Add(new EmitInstantaneously(baseProperties, emitterInfo));
                        break;
                    default:
                        Console.WriteLine($"Unsupported emitter class '{emitterClass}'.");
                        break;
                }
            }

            Emitters = emitters;
        }

        private void SetupInitializers(IEnumerable<IKeyValueCollection> initializerData)
        {
            Initializers = new List<IParticleInitializer>();
        }

        private void SetupOperators(IEnumerable<IKeyValueCollection> operatorData)
        {
            var operators = new List<IParticleOperator>();

            foreach (var emitterInfo in operatorData)
            {
                var operatorClass = emitterInfo.GetProperty<string>("_class");
                switch (operatorClass)
                {
                    case "C_OP_Decay":
                        operators.Add(new LifespanDecay(emitterInfo));
                        break;
                    case "C_OP_BasicMovement":
                        operators.Add(new MovementBasic(emitterInfo));
                        break;
                    case "C_OP_InterpolateRadius":
                        operators.Add(new RadiusScale(emitterInfo));
                        break;
                    default:
                        Console.WriteLine($"Unsupported operator class '{operatorClass}'.");
                        break;
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
                switch (rendererClass)
                {
                    case "C_OP_RenderSprites":
                        renderers.Add(new RenderSprites(rendererInfo, vrfGuiContext));
                        break;
                    default:
                        Console.WriteLine($"Unsupported renderer class '{rendererClass}'.");
                        break;
                }
            }

            Renderers = renderers;
        }
    }
}
