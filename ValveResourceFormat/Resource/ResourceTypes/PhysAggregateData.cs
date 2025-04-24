using System.Globalization;
using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class PhysAggregateData : KeyValuesOrNTRO
    {
        public int Flags
            => Data.GetInt32Property("m_nFlags");

        public Matrix4x4[] BindPose
           => Data.GetArray("m_bindPose")
                .Select(v => Matrix4x4FromArray(v
                    .Select(m => Convert.ToSingle(m.Value, CultureInfo.InvariantCulture))
                    .ToArray()))
                .ToArray();

        public Part[] Parts
            => parts ??= Data.GetArray("m_parts").Select(p => new Part(p)).ToArray();

        public uint[] SurfacePropertyHashes
            => Data.GetArray<object>("m_surfacePropertyHashes").Select(Convert.ToUInt32).ToArray();

        public IReadOnlyList<KVObject> CollisionAttributes
            => Data.GetArray("m_collisionAttributes");

        private Part[] parts;

        public PhysAggregateData()
        {
        }

        public PhysAggregateData(BlockType type) : base(type, "VPhysXAggregateData_t")
        {
        }

        static Matrix4x4 Matrix4x4FromArray(float[] a)
            => new(a[0], a[4], a[8], 0,
                   a[1], a[5], a[9], 0,
                   a[2], a[6], a[10], 0,
                   a[3], a[7], a[11], 1);
    }
}
