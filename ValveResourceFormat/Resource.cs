using System;
using System.Collections.Generic;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a Valve resource.
    /// </summary>
    public class Resource
    {
        /// <summary>
        /// Resource name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Resource size.
        /// </summary>
        public uint FileSize { get; set; }

        public uint Unknown1 { get; set; }
        public uint Unknown2 { get; set; } // Always appears to be 8

        /// <summary>
        /// A list of blocks this resource has.
        /// </summary>
        public readonly List<Block> Blocks;

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/> class.
        /// </summary>
        public Resource()
        {
            Blocks = new List<Block>();
        }
    }
}
