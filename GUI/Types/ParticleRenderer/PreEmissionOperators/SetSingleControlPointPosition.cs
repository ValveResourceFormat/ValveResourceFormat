using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetSingleControlPointPosition : IParticlePreEmissionOperator
    {
        private readonly int CP1 = 1;
        private readonly Vector3 CP1Pos = new(128, 0, 0);

        private readonly bool SetOnce;
        private readonly bool UseWorldLocation;
        private readonly int CPOffset;
        // The m_bUseWorldLocation parameter would set the CP positions in world space instead of object space. How do we do that?

        private bool HasRunBefore;

        public SetSingleControlPointPosition(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nCP1"))
            {
                CP1 = keyValues.GetInt32Property("m_nCP1");
            }

            if (keyValues.ContainsKey("m_vecCP1Pos"))
            {
                CP1Pos = keyValues.GetArray<double>("m_vecCP1Pos").ToVector3();
            }

            if (keyValues.ContainsKey("m_bSetOnce"))
            {
                SetOnce = keyValues.GetProperty<bool>("m_bSetOnce");
            }

            if (keyValues.ContainsKey("m_bUseWorldLocation"))
            {
                UseWorldLocation = keyValues.GetProperty<bool>("m_bUseWorldLocation");
            }

            if (keyValues.ContainsKey("m_nHeadLocation"))
            {
                CPOffset = keyValues.GetInt32Property("m_nHeadLocation");
            }
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(SetOnce && HasRunBefore))
            {
                // not fully accurate, as it's still in local space, but it's closer to correct
                var controlPointOffset = UseWorldLocation
                    ? Vector3.Zero
                    : particleSystemState.GetControlPoint(CPOffset).Position;

                particleSystemState.SetControlPointValue(CP1, CP1Pos + controlPointOffset);

                HasRunBefore = true;
            }
        }
    }
}
