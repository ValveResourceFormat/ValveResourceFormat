using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a capsule shape.
    /// </summary>
    public struct Capsule
    {
        /// <summary>
        /// Gets or sets the center points of the capsule.
        /// </summary>
        public Vector3[] Center { get; set; }
        /// <summary>
        /// Gets or sets the radius of the capsule.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Capsule"/> struct.
        /// </summary>
        public Capsule(KVObject data)
        {
            Center = data.GetArray("m_vCenter").Select(v => v.ToVector3()).ToArray();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
}
