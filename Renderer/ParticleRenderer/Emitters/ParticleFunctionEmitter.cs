namespace GUI.Types.ParticleRenderer.Emitters
{
    abstract class ParticleFunctionEmitter : ParticleFunction
    {
        protected ParticleFunctionEmitter(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract void Start(Action particleEmitCallback);

        public abstract void Stop();

        public abstract void Emit(float frameTime);

        public abstract bool IsFinished { get; protected set; }
    }
}
