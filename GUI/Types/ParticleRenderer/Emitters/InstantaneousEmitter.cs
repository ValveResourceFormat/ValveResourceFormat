using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public class InstantaneousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private readonly IKeyValueCollection baseProperties;

        private Action<Particle> particleEmitCallback;

        private INumberProvider emitCount;
        private float startTime;

        private float time;

        public InstantaneousEmitter(IKeyValueCollection baseProperties, IKeyValueCollection keyValues)
        {
            this.baseProperties = baseProperties;

            emitCount = keyValues.GetNumberProvider("m_nParticlesToEmit");
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
                var numToEmit = emitCount.NextInt(); // Get value from number provider
                for (var i = 0; i < numToEmit; i++)
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
