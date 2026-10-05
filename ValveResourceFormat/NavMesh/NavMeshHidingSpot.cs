using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// A position inside a navigation area where an agent can hide, used by bots.
    /// </summary>
    public readonly struct NavMeshHidingSpot
    {
        /// <summary>
        /// Gets the hiding spot identifier.
        /// </summary>
        public uint Id { get; init; }

        /// <summary>
        /// Gets the position of the hiding spot.
        /// </summary>
        public Vector3 Position { get; init; }

        /// <summary>
        /// Gets the classification flags of the hiding spot.
        /// </summary>
        public NavHidingSpotFlags Flags { get; init; }

        /// <summary>
        /// Reads a hiding spot from a binary reader.
        /// </summary>
        public static NavMeshHidingSpot Read(BinaryReader binaryReader)
        {
            return new NavMeshHidingSpot
            {
                Id = binaryReader.ReadUInt32(),
                Position = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle()),
                Flags = (NavHidingSpotFlags)binaryReader.ReadByte(),
            };
        }

        /// <summary>
        /// Reads a hiding spot from the bot analysis in <see cref="NavMeshFile.CustomData"/>.
        /// </summary>
        public static NavMeshHidingSpot FromKV(KVObject data)
        {
            return new NavMeshHidingSpot
            {
                Id = data.GetUInt32Property("id"),
                Position = data.GetSubCollection("pos").ToVector3(),
                Flags = (NavHidingSpotFlags)data.GetByteProperty("flags"),
            };
        }
    }
}
