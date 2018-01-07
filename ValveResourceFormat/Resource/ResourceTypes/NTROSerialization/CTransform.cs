using System;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    /// <summary>
    /// Represents a transformation matrix.
    /// </summary>
    public class CTransform
    {
        /// <summary>
        /// Gets the matrix values.
        /// </summary>
        public float[] Values { get; }

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
            Values = new[]
            {
                field0,
                field1,
                field2,
                field3,
                field4,
                field5,
                field6,
                field7,
            };
        }

        public override string ToString()
        {
            // http://stackoverflow.com/a/15085178/2200891
            return string.Format("q={{{0:F2}, {1:F2}, {2:F2}; w={3}}} p={{{4:F2}, {5:F2}, {6}}}", Values[4], Values[5], Values[6], Values[7].ToString("F2"), Values[0], Values[1], Values[2].ToString("F2"));
        }
    }
}
