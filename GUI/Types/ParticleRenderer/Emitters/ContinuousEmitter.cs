using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public class ContinuousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private readonly IKeyValueCollection baseProperties;

        private Action<Particle> particleEmitCallback;

        private long particleCount;
        private float time;

        public ContinuousEmitter(IKeyValueCollection baseProperties, IKeyValueCollection keyValues)
        {
            this.baseProperties = baseProperties;
        }

        public void Start(Action<Particle> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            particleCount = 0;
            time = 0f;

            IsFinished = false;
        }

        public void Stop()
        {
            IsFinished = true;
        }

        public void Update(float frameTime)
        {
            if (IsFinished)
            {
                return;
            }

            time += frameTime;

            var particle = new Particle(baseProperties);
            particle.ParticleCount = ++particleCount;
            particleEmitCallback(particle);
        }
    }
}
