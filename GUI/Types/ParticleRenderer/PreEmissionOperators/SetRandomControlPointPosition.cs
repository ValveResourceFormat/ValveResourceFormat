using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetRandomControlPointPosition : IParticlePreEmissionOperator
    {
        private readonly int cp = 1;
        private readonly Vector3 minPos = Vector3.Zero;
        private readonly Vector3 maxPos = Vector3.Zero;

        // The m_bUseWorldLocation parameter would set the CP positions in world space instead of object space. How do we do that?
        private readonly bool useWorldLocation;
        private readonly bool orient;
        private readonly int offsetCP;

        private readonly float reRandomRate = -1.0f;
        private readonly float interpolation = 1.0f;

        public SetRandomControlPointPosition(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nCP1", cp);
            minPos = parse.Vector3("m_vecCPMinPos", minPos);
            maxPos = parse.Vector3("m_vecCPMaxPos", maxPos);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            offsetCP = parse.Int32("m_nHeadLocation", offsetCP);
            orient = parse.Boolean("m_bOrient", orient);
            reRandomRate = parse.Float("m_flReRandomRate", reRandomRate);
            interpolation = parse.Float("m_flInterpolation", interpolation);
        }

        private bool HasRunBefore;
        private float timeSinceLastRun;

        private Vector3 currentPosition = Vector3.Zero;

        private void GenerateNewPosition()
        {
            currentPosition = MathUtils.RandomBetweenPerComponent(minPos, maxPos);
        }
        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            // not fully accurate, as it is still in local space, but it's closer to correct
            var controlPointOffset = useWorldLocation
                ? Vector3.Zero
                : particleSystemState.GetControlPoint(offsetCP).Position;

            var orientation = orient
                ? particleSystemState.GetControlPoint(offsetCP).Orientation
                : Vector3.Zero;


            // We need to start off with an initial value, regardless of interpolation
            if (!HasRunBefore)
            {
                GenerateNewPosition();

                particleSystemState.SetControlPointValue(cp, currentPosition + controlPointOffset);
                particleSystemState.SetControlPointOrientation(cp, orientation);
                HasRunBefore = true;
            }

            timeSinceLastRun += frameTime; // should we do this before or after?

            // just assuming that they wont take any negative number
            if (reRandomRate > 0f)
            {
                var lerpOld = particleSystemState.GetControlPoint(cp).Position;
                var lerpNew = currentPosition + controlPointOffset;

                // exponential fade like all the other lerps
                var positionBlended = MathUtils.Lerp(interpolation, lerpOld, lerpNew);

                // orientation doesn't lerp in the same way that position does

                particleSystemState.SetControlPointValue(cp, positionBlended);
                particleSystemState.SetControlPointOrientation(cp, orientation);

                // If we need to generate a new position
                if (timeSinceLastRun > reRandomRate)
                {
                    GenerateNewPosition();

                    // Subtract reRandomRate instead of resetting to 0 so we can maintain sub-frame timing
                    timeSinceLastRun -= reRandomRate;
                }
            }
        }
    }
}
