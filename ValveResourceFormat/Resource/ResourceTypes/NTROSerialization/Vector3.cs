using System;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class Vector3
    {
        public float field0 { get; }
        public float field1 { get; }
        public float field2 { get; }

        public Vector3(float field0, float field1, float field2)
        {
            this.field0 = field0;
            this.field1 = field1;
            this.field2 = field2;
        }

        public override string ToString()
        {
            return string.Format("({0:F6}, {1:F6}, {2:F6})", field0, field1, field2);
        }
    }
}
