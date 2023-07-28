using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class StopAfterDuration : IParticlePreEmissionOperator
    {
        private readonly INumberProvider duration = new LiteralNumberProvider(1.0f);
        private readonly bool destroy;

        public StopAfterDuration(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flDuration"))
            {
                duration = keyValues.GetNumberProvider("m_flDuration");
            }

            if (keyValues.ContainsKey("m_bDestroyImmediately"))
            {
                destroy = keyValues.GetProperty<bool>("m_bDestroyImmediately");
            }
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            particleSystemState.SetStopTime(duration.NextNumber(particleSystemState), destroy);
        }
    }
}
