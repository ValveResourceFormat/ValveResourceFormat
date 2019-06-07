using System;
using System.Collections.Generic;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using OpenTK;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleRenderer
    {
        public IEnumerable<IParticleEmitter> Emitters { get; private set; }

        public IEnumerable<IParticleInitializer> Initializers { get; private set; }

        public IEnumerable<IParticleOperator> Operators { get; private set; }

        public IEnumerable<IParticleRenderer> Renderers { get; private set; }

        private readonly ParticleSystem particleSystem;

        private readonly Camera camera;

        private readonly List<Particle> particles;

        private readonly ParticleGrid particleGrid;

        public ParticleRenderer(ParticleSystem particleSystem, GLRenderControl glControl)
        {
            this.particleSystem = particleSystem;

            particleGrid = new ParticleGrid(20, 5);

            camera = glControl.Camera;
            camera.SetViewportSize(glControl.Control.Width, glControl.Control.Height);
            camera.SetLocation(new Vector3(200));
            camera.LookAt(new Vector3(0));

            particles = new List<Particle>();

            glControl.Paint += (sender, args) =>
            {
                Update(args.FrameTime); // Couple update to painting for now
                Render();
            };

            SetupEmitters(particleSystem.GetEmitters());
            SetupInitializers(particleSystem.GetInitializers());
            SetupOperators(particleSystem.GetOperators());
            SetupRenderers(particleSystem.GetRenderers());

            Start();
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

            foreach (var emitter in Emitters)
            {
                emitter.Update(frameTime);
            }
        }

        public void Render()
        {
            particleGrid.Render(camera.ProjectionMatrix, camera.CameraViewMatrix);

            foreach (var renderer in Renderers)
            {
                renderer.Render(particles, camera.ProjectionMatrix, camera.CameraViewMatrix);
            }
        }

        private void SetupEmitters(IEnumerable<IKeyValueCollection> emitterData)
        {
            var emitters = new List<IParticleEmitter>();

            foreach (var emitterInfo in emitterData)
            {
                var emitterClass = emitterInfo.GetProperty<string>("_class");
                switch (emitterClass)
                {
                    case "C_OP_InstantaneousEmitter":
                        emitters.Add(new EmitInstantaneously(emitterInfo));
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
            Operators = new List<IParticleOperator>();
        }

        private void SetupRenderers(IEnumerable<IKeyValueCollection> rendererData)
        {
            Renderers = new List<IParticleRenderer>();
        }
    }
}
