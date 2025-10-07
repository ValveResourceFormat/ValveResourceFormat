using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// Represents a hitbox for collision detection in a model.
    /// </summary>
    public class Hitbox
    {
        /// <summary>
        /// The shape type of the hitbox.
        /// </summary>
        public enum HitboxShape
        {
#pragma warning disable CS1591
            Box,
            Sphere,
            Capsule,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Gets the name of the hitbox.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the surface property of the hitbox.
        /// </summary>
        public string SurfaceProperty { get; init; }

        /// <summary>
        /// Gets the name of the bone this hitbox is attached to.
        /// </summary>
        public string BoneName { get; init; }

        /// <summary>
        /// Gets the minimum bounds of the hitbox.
        /// </summary>
        public Vector3 MinBounds { get; init; }

        /// <summary>
        /// Gets the maximum bounds of the hitbox.
        /// </summary>
        public Vector3 MaxBounds { get; init; }

        /// <summary>
        /// Gets the radius of the hitbox shape.
        /// </summary>
        public float ShapeRadius { get; init; }

        /// <summary>
        /// Gets the group ID of the hitbox.
        /// </summary>
        public int GroupId { get; init; }

        /// <summary>
        /// Gets the shape type of the hitbox.
        /// </summary>
        public HitboxShape ShapeType { get; init; }

        /// <summary>
        /// Gets a value indicating whether this hitbox only supports translation.
        /// </summary>
        public bool TranslationOnly { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Hitbox"/> class from KeyValues data.
        /// </summary>
        /// <param name="data">The KeyValues data containing hitbox information.</param>
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
