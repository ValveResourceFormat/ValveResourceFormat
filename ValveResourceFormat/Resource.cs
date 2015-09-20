using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a Valve resource.
    /// </summary>
    public class Resource
    {
        private const ushort KNOWN_HEADER_VERSION = 12;

        private BinaryReader Reader;

        /// <summary>
        /// Resource name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Resource size.
        /// </summary>
        public uint FileSize { get; private set; }

        public ushort HeaderVersion { get; private set; }
        public ushort Version { get; private set; }

        /// <summary>
        /// A list of blocks this resource has.
        /// </summary>
        public readonly Dictionary<BlockType, Block> Blocks;

        /// <summary>
        /// Gets or sets the type of the resource.
        /// </summary>
        /// <value>The type of the resource.</value>
        public ResourceType ResourceType { get; set; }

        public ResourceExtRefList ExternalReferences;
        public ResourceEditInfo EditInfo;
        public ResourceIntrospectionManifest IntrospectionManifest;

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/> class.
        /// </summary>
        public Resource()
        {
            ResourceType = ResourceType.Unknown;
            Blocks = new Dictionary<BlockType, Block>();
        }

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        ~Resource()
        {
            if (Reader != null)
            {
                Reader.Dispose();

                Reader = null;
            }
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            Reader = new BinaryReader(input);

            FileSize = Reader.ReadUInt32();

            if (FileSize == Package.MAGIC)
            {
                throw new InvalidDataException("Use Package() class to parse VPK files.");
            }

            // TODO: Some real files seem to have different file size
            if (FileSize != Reader.BaseStream.Length)
            {
                //throw new Exception(string.Format("File size does not match size specified in file. {0} != {1}", FileSize, Reader.BaseStream.Length));
            }

            HeaderVersion = Reader.ReadUInt16();

            if (HeaderVersion != KNOWN_HEADER_VERSION)
            {
                throw new InvalidDataException(string.Format("Bad header version. ({0} != expected {1})", HeaderVersion, KNOWN_HEADER_VERSION));
            }

            Version = Reader.ReadUInt16();

            var blockOffset = Reader.ReadUInt32();
            var blockCount = Reader.ReadUInt32();

            Reader.BaseStream.Position += blockOffset - 8; // 8 is 2 uint32s we just read

            while (blockCount-- > 0)
            {
                var blockType = Encoding.UTF8.GetString(Reader.ReadBytes(4));
                var block = ConstructFromType(blockType);

                var position = Reader.BaseStream.Position;

                // Offset is relative to current position
                block.Offset = (uint)position + Reader.ReadUInt32();
                block.Size = Reader.ReadUInt32();

                block.Read(Reader, this);

                switch (block.GetChar())
                {
                    case BlockType.REDI:
                        EditInfo = (ResourceEditInfo)block;

                        // Try to determine resource type by looking at first compiler indentifier
                        if (EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                        {
                            var specialDeps = (Blocks.ResourceEditInfoStructs.SpecialDependencies)EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];

                            if (specialDeps.List.Count > 0)
                            {
                                ResourceType = DetermineResourceTypeByCompilerIdentifier(specialDeps.List[0].CompilerIdentifier);
                            }
                        }

                        break;

                    case BlockType.RERL:
                        ExternalReferences = (ResourceExtRefList)block;
                        break;

                    case BlockType.NTRO:
                        IntrospectionManifest = (ResourceIntrospectionManifest)block;
                        break;
                }

                Blocks.Add(block.GetChar(), block);

                Reader.BaseStream.Position = position + 8;
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                Read(fs);
            }
        }

        private Block ConstructFromType(string input)
        {
            switch (input)
            {
                case "DATA":
                    return ConstructResourceType();

                case "REDI":
                    return new ResourceEditInfo();

                case "RERL":
                    return new ResourceExtRefList();

                case "NTRO":
                    return new ResourceIntrospectionManifest();

                case "VBIB":
                    return new VBIB();
            }

            throw new ArgumentException(string.Format("Unrecognized block type '{0}'", input));
        }

        private ResourceData ConstructResourceType()
        {
            if (Blocks.ContainsKey(BlockType.NTRO))
            {
                return new ResourceTypes.NTRO();
            }

            switch (ResourceType)
            {
                case ResourceType.Panorama:
                    return new ResourceTypes.Panorama();
            }

            return new ResourceData();
        }

        private static ResourceType DetermineResourceTypeByCompilerIdentifier(string identifier)
        {
            if (identifier.StartsWith("Compile", StringComparison.Ordinal))
            {
                identifier = identifier.Remove(0, "Compile".Length);
            }

            ResourceType resourceType;

            if (Enum.TryParse(identifier, false, out resourceType))
            {
                return resourceType;
            }

            return ResourceType.Unknown;
        }
    }
}
