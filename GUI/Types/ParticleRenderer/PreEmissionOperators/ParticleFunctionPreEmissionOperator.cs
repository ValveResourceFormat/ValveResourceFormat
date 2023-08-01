namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    abstract class ParticleFunctionPreEmissionOperator : ParticleFunction
    {
        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        public abstract void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime);
    }
}
