namespace GUI.Types.ParticleRenderer.Operators
{
    abstract class ParticleFunctionOperator : ParticleFunction
    {
        public abstract void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}
