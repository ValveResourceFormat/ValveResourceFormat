using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public class InstantaneousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private readonly IKeyValueCollection baseProperties;

        private Action<Particle> particleEmitCallback;

        private long emitCount;
        private float startTime;

        private float time;

        public InstantaneousEmitter(IKeyValueCollection baseProperties, IKeyValueCollection keyValues)
        {
            this.baseProperties = baseProperties;

            emitCount = keyValues.GetIntegerProperty("m_nParticlesToEmit");
            startTime = keyValues.GetFloatProperty("m_flStartTime");
        }

        public void Start(Action<Particle> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            IsFinished = false;

            time = 0;
        }

        public void Stop()
        {
        }

        public void Update(float frameTime)
        {
            time += frameTime;

            if (!IsFinished && time >= startTime)
            {
                for (var i = 0; i < emitCount; i++)
                {
                    var particle = new Particle(baseProperties);
                    particle.ParticleCount = i;
                    particleEmitCallback(particle);
                }

                IsFinished = true;
            }
        }
    }
}
