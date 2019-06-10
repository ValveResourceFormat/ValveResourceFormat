using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public class ContinuousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private readonly IKeyValueCollection baseProperties;

        private readonly float emissionDuration = 0f;
        private readonly float startTime = 0f;
        private readonly float emitRate = 100f;
        private readonly float emitInterval = 0.01f;

        private Action<Particle> particleEmitCallback;

        private long particleCount;
        private float time;
        private float lastEmissionTime;

        public ContinuousEmitter(IKeyValueCollection baseProperties, IKeyValueCollection keyValues)
        {
            this.baseProperties = baseProperties;

            if (keyValues.ContainsKey("m_flEmissionDuration"))
            {
                emissionDuration = keyValues.GetFloatProperty("m_flEmissionDuration");
            }

            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEmitRate"))
            {
                emitRate = keyValues.GetFloatProperty("m_flEmitRate");
                emitInterval = 1 / emitRate;
            }
        }

        public void Start(Action<Particle> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            particleCount = 0;
            time = 0f;
            lastEmissionTime = 0;

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

            if (time >= startTime && (emissionDuration == 0f || time <= startTime + emissionDuration))
            {
                var numToEmit = (int)Math.Floor((time - lastEmissionTime) / emitInterval);
                var emitCount = Math.Min(5 * emitRate, numToEmit); // Limit the amount of particles to emit at once in case of refocus
                for (var i = 0; i < emitCount; i++)
                {
                    var particle = new Particle(baseProperties);
                    particle.ParticleCount = ++particleCount;
                    particleEmitCallback(particle);
                }

                lastEmissionTime += numToEmit * emitInterval;
            }
        }
    }
}
