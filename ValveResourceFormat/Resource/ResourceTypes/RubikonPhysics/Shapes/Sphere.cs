using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a sphere shape.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/hlvr/physicslib/RnSphere_t">RnSphere_t</seealso>
    public struct Sphere
    {
        /// <summary>
        /// Gets or sets the center of the sphere.
        /// </summary>
        public Vector3 Center { get; set; }
        /// <summary>
        /// Gets or sets the radius of the sphere.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sphere"/> struct.
        /// </summary>
        public Sphere(KVObject data)
        {
            Center = data.GetSubCollection("m_vCenter").ToVector3();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
}
