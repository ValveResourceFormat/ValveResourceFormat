using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a control point's orientation to a QAngle, in world space or relative to a head control
    /// point, optionally randomized and blended toward it every step. The randomized angle is drawn
    /// once when the system is created and kept through restarts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointOrientation">C_OP_SetControlPointOrientation</seealso>
    class SetControlPointOrientation : ParticleFunctionPreEmissionOperator
    {
        private readonly bool useWorldLocation;
        private readonly bool randomize;
        private readonly bool setOnce;
        private readonly int cp = 1;
        private readonly int headLocation;
        private readonly Vector3 rotation;
        private readonly Vector3 rotationB;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        private bool hasRunBefore;
        private Vector3 resolvedRotation;
        private bool zeroRotation;

        public SetControlPointOrientation(ParticleDefinitionParser parse) : base(parse)
        {
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            randomize = parse.Boolean("m_bRandomize", randomize);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            cp = Math.Clamp(parse.Int32("m_nCP", cp), 0, ParticleSystemState.MaxControlPoint);
            headLocation = Math.Clamp(parse.Int32("m_nHeadLocation", headLocation), 0, ParticleSystemState.MaxControlPoint);
            rotation = parse.Vector3("m_vecRotation", rotation);
            rotationB = parse.Vector3("m_vecRotationB", rotationB);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            resolvedRotation = randomize
                ? particleSystemState.Random.NextBetweenPerComponent(rotation, rotationB)
                : rotation;

            hasRunBefore = false;
            zeroRotation = rotation == Vector3.Zero && (!randomize || rotationB == Vector3.Zero);
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
            hasRunBefore = false;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (setOnce)
            {
                if (hasRunBefore)
                {
                    return;
                }

                hasRunBefore = true;
            }

            var lerpAmount = interpolation.NextNumber(particleSystemState);
            var target = EntityTransformHelper.EulerAnglesToQuaternion(resolvedRotation);
            var controlPoint = particleSystemState.GetControlPoint(cp);

            if (!useWorldLocation)
            {
                if (headLocation == cp)
                {
                    return;
                }

                var head = particleSystemState.GetControlPoint(headLocation);

                if (zeroRotation && lerpAmount == 1f)
                {
                    controlPoint.SetOrientation(head.Forward, head.Left, head.Up);
                    return;
                }

                var headRotation = head.GetRotation();

                if (Quaternion.Dot(target, headRotation) < 0f)
                {
                    target = -target;
                }

                target = headRotation * target;
            }

            if (!setOnce && lerpAmount != 1f)
            {
                target = ParticleMath.Blend(controlPoint.GetRotation(), target, lerpAmount);
            }

            controlPoint.SetOrientation(target);
        }
    }
}
