namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    interface IParticlePreEmissionOperator
    {
        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime);
    }
}
