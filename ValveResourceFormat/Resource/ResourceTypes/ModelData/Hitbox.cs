using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    public class Hitbox
    {
        public enum HitboxShape
        {
#pragma warning disable CS1591
            Box,
            Sphere,
            Capsule,
#pragma warning restore CS1591
        }

        public string Name { get; init; }
        public string SurfaceProperty { get; init; }
        public string BoneName { get; init; }
        public Vector3 MinBounds { get; init; }
        public Vector3 MaxBounds { get; init; }
        public float ShapeRadius { get; init; }
        public int GroupId { get; init; }
        public HitboxShape ShapeType { get; init; }
        public bool TranslationOnly { get; init; }

        public Hitbox(KVObject data)
        {
            Name = data.GetStringProperty("m_name");
            SurfaceProperty = data.GetStringProperty("m_sSurfaceProperty");
            BoneName = data.GetStringProperty("m_sBoneName");
            MinBounds = data.GetSubCollection("m_vMinBounds").ToVector3();
            MaxBounds = data.GetSubCollection("m_vMaxBounds").ToVector3();
            TranslationOnly = data.GetProperty<bool>("m_bTranslationOnly");
            GroupId = data.GetInt32Property("m_nGroupId");

            ShapeType = (HitboxShape)data.GetInt32Property("m_nShapeType");
            ShapeRadius = data.GetFloatProperty("m_flShapeRadius");
        }
    }
}
