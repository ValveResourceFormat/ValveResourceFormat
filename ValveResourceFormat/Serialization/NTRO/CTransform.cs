using System;
using System.Numerics;

namespace ValveResourceFormat.Serialization.NTRO
{
    /// <summary>
    /// Represents a transformation matrix.
    /// </summary>
    public class CTransform
    {
        public Quaternion Q { get; }
        public Vector3 P { get; }
        public float Scale { get; } // TODO: Unconfirmed? resourceinfo appears to not print this field at all

        /// <summary>
        /// Initializes a new instance of the <see cref="CTransform"/> class.
        /// </summary>
        /// <param name="field0">First item of the matrix.</param>
        /// <param name="field1">Second item of the matrix.</param>
        /// <param name="field2">Third item of the matrix.</param>
        /// <param name="field3">Fourth item of the matrix.</param>
        /// <param name="field4">Fifth item of the matrix.</param>
        /// <param name="field5">Sixth item of the matrix.</param>
        /// <param name="field6">Seventh item of the matrix.</param>
        /// <param name="field7">Eighth item of the matrix.</param>
        public CTransform(float field0, float field1, float field2, float field3, float field4, float field5, float field6, float field7)
        {
            Q = new Quaternion(field4, field5, field6, field7);
            P = new Vector3(field0, field1, field2);
            Scale = field3;
        }

        public override string ToString()
        {
            // http://stackoverflow.com/a/15085178/2200891
            return string.Format("q={{{0:F2}, {1:F2}, {2:F2}; w={3}}} p={{{4:F2}, {5:F2}, {6}}}", Q.X, Q.Y, Q.Z, Q.W.ToString("F2"), P.X, P.Y, P.Z.ToString("F2"));
        }
    }
}
