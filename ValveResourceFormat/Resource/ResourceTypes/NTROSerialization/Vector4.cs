namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class Vector4
    {
        public float field0 { get; }
        public float field1 { get; }
        public float field2 { get; }
        public float field3 { get; }

        public Vector4(float field0, float field1, float field2, float field3)
        {
            this.field0 = field0;
            this.field1 = field1;
            this.field2 = field2;
            this.field3 = field3;
        }

        // Due to DataType needing to be known to do ToString() here, it is done elsewhere
    }
}
