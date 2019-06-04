using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public class EmitInstantaneously : IParticleEmitter
    {
        public long NumToEmit { get; private set; }

        private Action<Particle> particleEmitCallback;

        public EmitInstantaneously(IKeyValueCollection keyValues)
        {
            NumToEmit = keyValues.GetIntegerProperty("m_nParticlesToEmit");
        }

        public void Start(Action<Particle> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            for (var i = 0; i < NumToEmit; i++)
            {
                particleEmitCallback(new Particle());
            }
        }

        public void Stop()
        {
        }

        public void Update(float frameTime)
        {
        }
    }
}
