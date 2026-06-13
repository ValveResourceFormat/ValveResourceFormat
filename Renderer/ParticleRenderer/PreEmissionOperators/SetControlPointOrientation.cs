namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a control point orientation based on a QAngle, optionally randomizing and interpolating.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointOrientation">C_OP_SetControlPointOrientation</seealso>
    class SetControlPointOrientation : ParticleFunctionPreEmissionOperator
    {
        private readonly bool useWorldLocation;
        private readonly bool randomize;
        private readonly bool setOnce;
        private readonly int cp;
        private readonly int headLocation;
        private readonly Vector3 rotation;
        private readonly Vector3 rotationB;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        private bool hasRunBefore;

        public SetControlPointOrientation(ParticleDefinitionParser parse) : base(parse)
        {
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            randomize = parse.Boolean("m_bRandomize", randomize);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            cp = parse.Int32("m_nCP", 1);
            headLocation = parse.Int32("m_nHeadLocation", headLocation);
            rotation = parse.Vector3("m_vecRotation", rotation);
            rotationB = parse.Vector3("m_vecRotationB", rotationB);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (setOnce && hasRunBefore)
            {
                return;
            }

            var targetRotation = randomize
                ? ParticleCollection.RandomBetweenPerComponent(Random.Shared.Next(), rotation, rotationB)
                : rotation;

            var targetOrientation = GetOrientationFromQAngle(targetRotation);

            if (!useWorldLocation)
            {
                // Local orientation support is not fully accurate; use the head control point orientation as a fallback reference.
                var referenceOrientation = particleSystemState.GetControlPoint(headLocation).Orientation;
                if (referenceOrientation != Vector3.Zero)
                {
                    targetOrientation = Vector3.Normalize(targetOrientation + referenceOrientation);
                }
            }

            var currentOrientation = particleSystemState.GetControlPoint(cp).Orientation;
            var lerpAmount = interpolation.NextNumber(particleSystemState);
            var outputOrientation = Vector3.Lerp(currentOrientation, targetOrientation, lerpAmount);

            if (outputOrientation != Vector3.Zero)
            {
                outputOrientation = Vector3.Normalize(outputOrientation);
            }

            particleSystemState.SetControlPointOrientation(cp, outputOrientation);
            hasRunBefore = true;
        }

        private static Vector3 GetOrientationFromQAngle(Vector3 qAngle)
        {
            var rotationMatrix = ValveResourceFormat.Utils.EntityTransformHelper.CreateRotationMatrixFromEulerAngles(qAngle);
            return Vector3.Normalize(Vector3.Transform(new Vector3(1, 0, 0), rotationMatrix));
        }
    }
}
