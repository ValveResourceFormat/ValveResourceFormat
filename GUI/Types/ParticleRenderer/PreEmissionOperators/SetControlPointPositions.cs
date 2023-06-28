using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetControlPointPositions : IParticlePreEmissionOperator
    {
        private readonly int CP1 = 1;
        private readonly int CP2 = 2;
        private readonly int CP3 = 3;
        private readonly int CP4 = 4;
        private readonly Vector3 CP1Pos = new(128, 0, 0);
        private readonly Vector3 CP2Pos = new(0, -128, 0);
        private readonly Vector3 CP3Pos = new(-128, 0, 0);
        private readonly Vector3 CP4Pos = new(0, -128, 0);

        private readonly bool setOnce;
        private readonly bool useWorldLocation;
        private readonly int CPOffset;
        // The m_bUseWorldLocation parameter would set the CP positions in world space instead of object space. How do we do that?

        private bool HasRunBefore;

        public SetControlPointPositions(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nCP1"))
            {
                CP1 = keyValues.GetInt32Property("m_nCP1");
            }

            if (keyValues.ContainsKey("m_nCP2"))
            {
                CP2 = keyValues.GetInt32Property("m_nCP2");
            }

            if (keyValues.ContainsKey("m_nCP3"))
            {
                CP3 = keyValues.GetInt32Property("m_nCP3");
            }

            if (keyValues.ContainsKey("m_nCP4"))
            {
                CP4 = keyValues.GetInt32Property("m_nCP4");
            }

            if (keyValues.ContainsKey("m_nCP4"))
            {
                CP4 = keyValues.GetInt32Property("m_nCP4");
            }

            if (keyValues.ContainsKey("m_vecCP1Pos"))
            {
                CP1Pos = keyValues.GetArray<double>("m_vecCP1Pos").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecCP2Pos"))
            {
                CP2Pos = keyValues.GetArray<double>("m_vecCP2Pos").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecCP3Pos"))
            {
                CP3Pos = keyValues.GetArray<double>("m_vecCP3Pos").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecCP4Pos"))
            {
                CP4Pos = keyValues.GetArray<double>("m_vecCP4Pos").ToVector3();
            }

            if (keyValues.ContainsKey("m_bSetOnce"))
            {
                setOnce = keyValues.GetProperty<bool>("m_bSetOnce");
            }

            if (keyValues.ContainsKey("m_bUseWorldLocation"))
            {
                useWorldLocation = keyValues.GetProperty<bool>("m_bUseWorldLocation");
            }

            if (keyValues.ContainsKey("m_nHeadLocation"))
            {
                CPOffset = keyValues.GetInt32Property("m_nHeadLocation");
            }
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(setOnce && HasRunBefore))
            {
                // not fully accurate, as it is still in local space, but it's closer to correct
                var controlPointOffset = useWorldLocation
                    ? Vector3.Zero
                    : particleSystemState.GetControlPoint(CPOffset).Position;

                particleSystemState.SetControlPointValue(CP1, CP1Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP2, CP2Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP3, CP3Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP4, CP4Pos + controlPointOffset);

                HasRunBefore = true;
            }
        }
    }
}
