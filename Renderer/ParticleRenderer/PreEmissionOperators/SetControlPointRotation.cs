namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    class SetControlPointRotation : ParticleFunctionPreEmissionOperator
    {
        private readonly IVectorProvider axis = new LiteralVectorProvider(new Vector3(0, 0, 1));
        private readonly int cp;
        private readonly int localCP = -1; // ??
        private readonly INumberProvider rotationRate = new LiteralNumberProvider(180);

        public SetControlPointRotation(ParticleDefinitionParser parse) : base(parse)
        {
            axis = parse.VectorProvider("m_vecRotAxis", axis);
            rotationRate = parse.NumberProvider("m_flRotRate", rotationRate);
            cp = parse.Int32("m_nCP", cp);
            localCP = parse.Int32("m_nLocalCP", localCP);
        }
        private static Vector3 MatrixMul(Vector3 vector, Matrix4x4 rotatedMatrix)
        {
            return vector.X * new Vector3(rotatedMatrix.M11, rotatedMatrix.M12, rotatedMatrix.M13) +
                vector.Y * new Vector3(rotatedMatrix.M21, rotatedMatrix.M22, rotatedMatrix.M23) +
                vector.Z * new Vector3(rotatedMatrix.M31, rotatedMatrix.M32, rotatedMatrix.M33);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var axis = this.axis.NextVector(particleSystemState);
            var rotationRate = this.rotationRate.NextNumber(particleSystemState);
            // probably slow but who knows???
            var rotatedVector = MatrixMul(new Vector3(1, 0, 0), Matrix4x4.CreateFromAxisAngle(axis, rotationRate * frameTime));

            particleSystemState.GetControlPoint(cp).Orientation = Vector3.Normalize(rotatedVector);
        }
    }
}
