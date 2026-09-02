using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Represents a physics part.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysXBodyPart_t">VPhysXBodyPart_t</seealso>
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
        /// Gets or sets the inertia scale.
        /// </summary>
        public float InertiaScale { get; set; }
        /// <summary>
        /// Gets or sets the linear damping.
        /// </summary>
        public float LinearDamping { get; set; }
        /// <summary>
        /// Gets or sets the angular damping.
        /// </summary>
        public float AngularDamping { get; set; }
        /// <summary>
        /// Gets or sets whether <see cref="MassCenterOverride"/> replaces the shape-derived centre of mass.
        /// </summary>
        public bool OverrideMassCenter { get; set; }
        /// <summary>
        /// Gets or sets the centre of mass override, used when <see cref="OverrideMassCenter"/> is set.
        /// </summary>
        public Vector3 MassCenterOverride { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Part"/> struct.
        /// </summary>
        public Part(KVObject data)
        {
            Flags = data.GetInt32Property("m_nFlags");
            Mass = data.GetFloatProperty("m_flMass");
            Shape = new Shape(data.GetSubCollection("m_rnShape"));
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
            InertiaScale = data.GetFloatProperty("m_flInertiaScale", 1f);
            LinearDamping = data.GetFloatProperty("m_flLinearDamping");
            AngularDamping = data.GetFloatProperty("m_flAngularDamping");
            OverrideMassCenter = data.GetBooleanProperty("m_bOverrideMassCenter");
            MassCenterOverride = data.GetSubCollection("m_vMassCenterOverride")?.ToVector3() ?? default;
        }
    }
}
