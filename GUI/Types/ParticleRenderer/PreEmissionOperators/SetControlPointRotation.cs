using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetControlPointRotation : IParticlePreEmissionOperator
    {
        private readonly IVectorProvider axis = new LiteralVectorProvider(new Vector3(0, 0, 1));
        private readonly int cp;
        private readonly int localCP = -1; // ??
        private readonly INumberProvider rotationRate = new LiteralNumberProvider(180);

        public SetControlPointRotation(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_vecRotAxis"))
            {
                axis = keyValues.GetVectorProvider("m_vecRotAxis");
            }

            if (keyValues.ContainsKey("m_flRotRate"))
            {
                rotationRate = keyValues.GetNumberProvider("m_flRotRate");
            }

            if (keyValues.ContainsKey("m_nCP"))
            {
                cp = keyValues.GetInt32Property("m_nCP");
            }

            if (keyValues.ContainsKey("m_nLocalCP"))
            {
                localCP = keyValues.GetInt32Property("m_nLocalCP");
            }
        }
        private static Vector3 MatrixMul(Vector3 vector, Matrix4x4 rotatedMatrix)
        {
            return vector.X * new Vector3(rotatedMatrix.M11, rotatedMatrix.M12, rotatedMatrix.M13) +
                vector.Y * new Vector3(rotatedMatrix.M21, rotatedMatrix.M22, rotatedMatrix.M23) +
                vector.Z * new Vector3(rotatedMatrix.M31, rotatedMatrix.M32, rotatedMatrix.M33);
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var axis = this.axis.NextVector(particleSystemState);
            var rotationRate = this.rotationRate.NextNumber(particleSystemState);
            // probably slow but who knows???
            var rotatedVector = MatrixMul(new Vector3(1, 0, 0), Matrix4x4.CreateFromAxisAngle(axis, rotationRate * frameTime));

            particleSystemState.GetControlPoint(cp).Orientation = Vector3.Normalize(rotatedVector);
        }
    }
}
