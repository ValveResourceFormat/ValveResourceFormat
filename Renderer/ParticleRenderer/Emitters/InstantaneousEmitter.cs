namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    class InstantaneousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private Action? particleEmitCallback;

        private readonly INumberProvider emitCount = new LiteralNumberProvider(1);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        private float time;

        public InstantaneousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emitCount = parse.NumberProvider("m_nParticlesToEmit", emitCount);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
        }

        public override void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            IsFinished = false;

            time = 0;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime)
        {
            if (IsFinished)
            {
                return;
            }

            time += frameTime;

            if (time >= startTime.NextNumber())
            {
                var numToEmit = (int)emitCount.NextNumber(); // Get value from number provider
                for (var i = 0; i < numToEmit; i++)
                {
                    particleEmitCallback?.Invoke();
                }

                IsFinished = true;
            }
        }
    }
}
