using System;
using System.IO;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a block within the resource file.
    /// </summary>
    public abstract class Block
    {
        /// <summary>
        /// Offset to the data.
        /// </summary>
        public uint Offset { get; set; }

        /// <summary>
        /// Data size.
        /// </summary>
        public uint Size { get; set; }

        public abstract BlockType GetChar();
        public abstract void Read(BinaryReader reader);
        public abstract override string ToString();

        /// <summary>
        /// Returns a class for given block type.
        /// </summary>
        /// <param name="input">Block type.</param>
        /// <param name="resourceType">Resource type (for DATA block only).</param>
        /// <returns>
        /// Constructed block type object.
        /// </returns>
        public static Block ConstructFromType(string input, ResourceType resourceType)
        {
            switch (input)
            {
                case "DATA":
                    return ConstructResourceType(resourceType);

                case "REDI":
                    return new Blocks.ResourceEditInfo();

                case "RERL":
                    return new Blocks.ResourceExtRefList();

                case "NTRO":
                    return new Blocks.ResourceIntrospectionManifest();

                case "VBIB":
                    return new Blocks.VBIB();
            }

            throw new ArgumentException(string.Format("Unrecognized block type '{0}'", input));
        }

        public static Blocks.ResourceData ConstructResourceType(ResourceType resourceType)
        {
            switch (resourceType)
            {
                case ResourceType.Panorama:
                    return new ResourceTypes.Panorama();
            }

            return new Blocks.ResourceData();
        }
    }
}
