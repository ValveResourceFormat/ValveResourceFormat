namespace ValveResourceFormat.Serialization.NTRO
{
    /// <summary>
    /// Represents a 3x4 matrix.
    /// </summary>
    public class Matrix3x4
    {
        /// <summary>
        /// Gets the matrix values.
        /// </summary>
        public float[] Values { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Matrix3x4"/> class.
        /// </summary>
        /// <param name="field0">First item of the matrix.</param>
        /// <param name="field1">Second item of the matrix.</param>
        /// <param name="field2">Third item of the matrix.</param>
        /// <param name="field3">Fourth item of the matrix.</param>
        /// <param name="field4">Fifth item of the matrix.</param>
        /// <param name="field5">Sixth item of the matrix.</param>
        /// <param name="field6">Seventh item of the matrix.</param>
        /// <param name="field7">Eighth item of the matrix.</param>
        /// <param name="field8">Nineth item of the matrix.</param>
        /// <param name="field9">Tenth item of the matrix.</param>
        /// <param name="field10">Eleventh item of the matrix.</param>
        /// <param name="field11">Twelfth item of the matrix.</param>
        public Matrix3x4(
            float field0,
            float field1,
            float field2,
            float field3,
            float field4,
            float field5,
            float field6,
            float field7,
            float field8,
            float field9,
            float field10,
            float field11)
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
                field8,
                field9,
                field10,
                field11,
            };
        }

        public override string ToString()
        {
            using (var writer = new IndentedTextWriter())
            {
                WriteText(writer);

                return writer.ToString();
            }
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine();
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", Values[0], Values[1], Values[2], Values[3]);
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", Values[4], Values[5], Values[6], Values[7]);
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", Values[8], Values[9], Values[10], Values[11]);
        }
    }
}
