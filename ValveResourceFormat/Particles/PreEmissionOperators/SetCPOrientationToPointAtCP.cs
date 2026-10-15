using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Turns a control point to face another one, or away from it with <c>m_bPointAway</c>, blending
    /// from its current orientation by <c>m_flInterpolation</c> every step. <c>m_b2DOrientation</c>
    /// ignores the height difference, and <c>m_bAvoidSingularity</c> builds the side axes from a
    /// perpendicular that stays stable when the direction is vertical.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetCPOrientationToPointAtCP">C_OP_SetCPOrientationToPointAtCP</seealso>
    class SetCPOrientationToPointAtCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int inputCP = 1;
        private readonly int outputCP;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);
        private readonly bool orientation2D;
        private readonly bool avoidSingularity;
        private readonly bool pointAway;

        public SetCPOrientationToPointAtCP(ParticleDefinitionParser parse) : base(parse)
        {
            inputCP = parse.Int32("m_nInputCP", inputCP);
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            orientation2D = parse.Boolean("m_b2DOrientation", orientation2D);
            avoidSingularity = parse.Boolean("m_bAvoidSingularity", avoidSingularity);
            pointAway = parse.Boolean("m_bPointAway", pointAway);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (inputCP == outputCP)
            {
                return;
            }

            var amount = interpolation.NextNumber(particleSystemState);
            var output = particleSystemState.GetControlPoint(outputCP);
            var target = particleSystemState.GetControlPoint(inputCP).Position;

            if (orientation2D)
            {
                target.Z = output.Position.Z;
            }

            var direction = pointAway ? output.Position - target : target - output.Position;
            var forward = MathUtils.SafeNormalize(direction, Vector3.UnitZ, 1e-10f);

            Vector3 left;
            Vector3 up;

            if (avoidSingularity)
            {
                left = StablePerpendicular(forward);
                up = Vector3.Cross(forward, left);
            }
            else
            {
                (left, up) = ParticleMath.VectorVectors(forward);
            }

            var targetRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                forward.X, forward.Y, forward.Z, 0f,
                left.X, left.Y, left.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                0f, 0f, 0f, 1f));

            output.SetOrientation(ParticleMath.Blend(output.GetRotation(), targetRotation, amount));
        }

        private static Vector3 StablePerpendicular(Vector3 forward)
        {
            var perpendicular = MathUtils.SafeNormalize(new Vector3(((1f - forward.Z) * (forward.Y * forward.Y)) + forward.Z, 0f, -forward.X));
            perpendicular -= forward * Vector3.Dot(perpendicular, forward);

            return MathUtils.SafeNormalize(perpendicular);
        }
    }
}
