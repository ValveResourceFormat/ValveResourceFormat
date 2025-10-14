using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Represents a physics part.
    /// </summary>
    public struct Part
    {
        /// <summary>
        /// Gets or sets the flags.
        /// </summary>
        public int Flags { get; set; }
        /// <summary>
        /// Gets or sets the mass.
        /// </summary>
        public float Mass { get; set; }
        /// <summary>
        /// Gets or sets the shape.
        /// </summary>
        public Shape Shape { get; set; }
        /// <summary>
        /// Gets or sets the collision attribute index.
        /// </summary>
        public int CollisionAttributeIndex { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Part"/> struct.
        /// </summary>
        public Part(KVObject data)
        {
            Flags = data.GetInt32Property("m_nFlags");
            Mass = data.GetFloatProperty("m_flMass");
            Shape = new Shape(data.GetSubCollection("m_rnShape"));
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
        }
    }
}
