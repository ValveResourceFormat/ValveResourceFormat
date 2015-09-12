using System;

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

        /// <summary>
        /// Returns a class for given block type.
        /// </summary>
        /// <param name="input">Block type.</param>
        /// <returns>
        /// Constructed block type object.
        /// </returns>
        public static Block ConstructFromType(string input)
        {
            switch (input)
            {
                case "DATA":
                    return new Blocks.Data();

                case "REDI":
                    return new Blocks.ResourceEditInfo();

                case "RERL":
                    return new Blocks.ResourceExtRefList();

                case "NTRO":
                    return new Blocks.ResourceIntrospectionManifest();
            }

            throw new ArgumentException(string.Format("Unrecognized block type '{0}'", input));
        }
    }
}
