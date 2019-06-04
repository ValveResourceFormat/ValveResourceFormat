using System.Collections.Generic;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.Renderer;
using OpenTK;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.ParticleRenderer
{
    public class ParticleRenderer
    {
        public IEnumerable<IParticleEmitter> Emitters { get; private set; }

        public IEnumerable<IParticleInitializer> Initializers { get; private set; }

        public IEnumerable<IParticleOperator> Operators { get; private set; }

        public IEnumerable<IParticleRenderer> Renderers { get; private set; }

        private readonly ParticleSystem particleSystem;

        private readonly Camera camera;

        private readonly List<Particle> particles;

        public ParticleRenderer(ParticleSystem particleSystem)
        {
            this.particleSystem = particleSystem;

            camera = new Camera(new Vector3(-20, -20, -20), new Vector3(20, 20, 20));
            particles = new List<Particle>();
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
            foreach (var renderer in Renderers)
            {
                renderer.Render(particles, camera.ProjectionMatrix, camera.CameraViewMatrix);
            }
        }
    }
}
