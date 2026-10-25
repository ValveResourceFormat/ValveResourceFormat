using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Traces down from a control point, starting <c>m_flTraceOffset</c> above it, and turns another
    /// control point so its forward axis is the ground normal, or straight up when nothing is hit
    /// within <c>m_flMaxTraceLength</c>. The trace is repeated only once the input control point has
    /// moved more than <c>m_flTolerance</c>. With a positive <c>m_flInterpRate</c> the orientation
    /// then eases toward each new normal for that many seconds, starting over from wherever the last
    /// step left it, so the motion depends on the frame rate and may stop short of the normal.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetCPOrientationToGroundNormal">C_OP_SetCPOrientationToGroundNormal</seealso>
    class SetCPOrientationToGroundNormal : ParticleFunctionOperator
    {
        private readonly float interpRate;
        private readonly float maxTraceLength = 128f;
        private readonly float toleranceSquared = 32f * 32f;
        private readonly float traceOffset = 64f;
        private readonly int inputControlPoint;
        private readonly int outputControlPoint = 1;

        private bool initialized;
        private Quaternion current;
        private Quaternion target;
        private Vector3 lastTracePosition;
        private float interpStartTime;

        public SetCPOrientationToGroundNormal(ParticleDefinitionParser parse) : base(parse)
        {
            interpRate = parse.Float("m_flInterpRate", interpRate);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            toleranceSquared = ParticleMath.Square(parse.Float("m_flTolerance", 32f));
            traceOffset = parse.Float("m_flTraceOffset", traceOffset);
            inputControlPoint = parse.Int32("m_nInputCP", inputControlPoint);
            outputControlPoint = parse.Int32("m_nOutputCP", outputControlPoint);
        }

        public override void Reset()
        {
            initialized = false;
            current = Quaternion.Identity;
            target = Quaternion.Identity;
            lastTracePosition = new Vector3(float.MaxValue);
            interpStartTime = 0f;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var input = particleSystemState.GetControlPoint(inputControlPoint);
            var output = particleSystemState.GetControlPoint(outputControlPoint);
            var time = particleSystemState.Age;

            if (!initialized)
            {
                current = target = frameTime == 0f ? input.GetRotation() : input.GetPreviousRotation();
                initialized = true;
            }

            var position = input.Position;

            if (Vector3.DistanceSquared(position, lastTracePosition) > toleranceSquared)
            {
                lastTracePosition = position;

                var start = position + new Vector3(0f, 0f, traceOffset);
                var end = start - new Vector3(0f, 0f, maxTraceLength);

                var forward = particleSystemState.Collision.TraceRay(start, end, out _, out var normal, out _) ? normal : Vector3.UnitZ;
                var rotation = EntityTransformHelper.ForwardDirectionToQuaternion(forward);

                output.SetOrientation(rotation);

                if (interpRate > 0f)
                {
                    target = rotation;
                    interpStartTime = time;
                }
            }

            var interpEnd = interpStartTime + interpRate;

            if (interpRate <= 0f || interpEnd <= time)
            {
                return;
            }

            current = Quaternion.Slerp(current, target, MathUtils.RemapValClamped(time, interpStartTime, interpEnd, 0f, 1f));
            output.SetOrientation(current);
        }
    }
}
