using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.ToolsAssetInfo
{
    /// <summary>
    /// Represents tools asset info data from Valve's Source 2 engine.
    /// </summary>
    public class ToolsAssetInfo
    {
        /// <summary>
        /// The root an asset path is relative to.
        /// </summary>
        public enum AssetLocation
        {
            /// <summary>The game root, containing the compiled assets.</summary>
            Game = 0,

            /// <summary>The content root, containing the source assets.</summary>
            Content = 1,

            /// <summary>No specific root, used by location agnostic paths and by resource names.</summary>
            None = 7,
        }

        /// <summary>
        /// Represents a file entry in the tools asset info.
        /// </summary>
        public class File
        {
            /// <summary>
            /// Represents an input dependency.
            /// </summary>
            public readonly struct InputDependency
            {
                /// <summary>
                /// Gets the filename.
                /// </summary>
                public string Filename { get; init; }

                /// <summary>
                /// Gets the file CRC.
                /// </summary>
                public uint FileCRC { get; init; }

                /// <summary>
                /// Gets a value indicating whether this dependency is optional.
                /// </summary>
                public bool Optional { get; init; }

                /// <summary>
                /// Gets a value indicating whether the file exists.
                /// </summary>
                public bool FileExists { get; init; }
            }

            /// <summary>
            /// Represents a search path.
            /// </summary>
            public readonly struct SearchPath
            {
                /// <summary>
                /// Gets the filename.
                /// </summary>
                public string Filename { get; init; }

                /// <summary>
                /// Gets the CRC32 of the file, or zero when it was not computed.
                /// </summary>
                public uint FileCRC { get; init; }

                /// <summary>
                /// Gets the last modification time of the file as a FILETIME. It has a resolution
                /// of 25.6 microseconds because the low 8 bits are not stored.
                /// </summary>
                [KVIgnore]
                public long ModificationTimeFileTime { get; init; }

                /// <summary>
                /// Gets the last modification time of the file, as an ISO 8601 string.
                /// </summary>
                public string ModificationTime => DateTime.FromFileTimeUtc(ModificationTimeFileTime).ToString("O", CultureInfo.InvariantCulture);

                /// <summary>
                /// Gets the size of the file in bytes.
                /// </summary>
                public long FileSize { get; init; }
            }

            /// <summary>
            /// Represents a special dependency.
            /// </summary>
            public struct SpecialDependency
            {
                /// <summary>
                /// Gets or sets the string value.
                /// </summary>
                public string String { get; set; }

                /// <summary>
                /// Gets or sets the compiler identifier.
                /// </summary>
                public string CompilerIdentifier { get; set; }

                /// <summary>
                /// Gets or sets the fingerprint.
                /// </summary>
                public uint Fingerprint { get; set; }

                /// <summary>
                /// Gets or sets the user data.
                /// </summary>
                public uint UserData { get; set; }
            }

            /// <summary>
            /// Represents a special input dependency (version 15+).
            /// </summary>
            public readonly struct SpecialInputDependency
            {
                /// <summary>
                /// Gets the compiler identifier, or an empty string when there is none.
                /// </summary>
                public string CompilerIdentifier { get; init; }

                /// <summary>
                /// Gets the special string.
                /// </summary>
                public string Special { get; init; }

                /// <summary>
                /// Gets the user data, which is a KV3 text string.
                /// </summary>
                public string UserData { get; init; }

                /// <summary>
                /// Gets the filename.
                /// </summary>
                public string Filename { get; init; }

                /// <summary>
                /// Gets the fingerprint.
                /// </summary>
                public uint Fingerprint { get; init; }
            }

            /// <summary>
            /// Gets or sets a value indicating whether the file needs refresh.
            /// </summary>
            public bool NeedsRefresh { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the file is invalid.
            /// </summary>
            public bool Invalid { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the file is up to date.
            /// </summary>
            public bool UpToDate { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether compilation failed.
            /// </summary>
            public bool CompileFailed { get; set; }

            /// <summary>
            /// Gets the search paths in the game root.
            /// </summary>
            public List<SearchPath> SearchPathsGameRoot { get; } = [];

            /// <summary>
            /// Gets the search paths in the content root.
            /// </summary>
            public List<SearchPath> SearchPathsContentRoot { get; } = [];

            /// <summary>
            /// Gets the input dependencies.
            /// </summary>
            public List<InputDependency> InputDependencies { get; } = [];

            /// <summary>
            /// Gets the additional input dependencies.
            /// </summary>
            public List<InputDependency> AdditionalInputDependencies { get; } = [];

            /// <summary>
            /// Gets the external references.
            /// </summary>
            public List<string> ExternalReferences { get; } = [];

            /// <summary>
            /// Gets the child resources.
            /// </summary>
            public List<string> ChildResources { get; } = [];

            /// <summary>
            /// Gets the additional related files.
            /// </summary>
            public List<string> AdditionalRelatedFiles { get; } = [];

            /// <summary>
            /// Gets the weak references.
            /// </summary>
            public List<string> WeakReferences { get; } = [];

            /// <summary>
            /// Gets the special dependencies.
            /// </summary>
            public List<SpecialDependency> SpecialDependencies { get; } = [];

            /// <summary>
            /// Gets the special input dependencies.
            /// </summary>
            public List<SpecialInputDependency> SpecialInputDependencies { get; } = [];

            /// <summary>
            /// Gets the searchable user data.
            /// </summary>
            public Dictionary<string, object> SearchableUserData { get; } = [];

            /// <summary>
            /// Gets the subasset definitions.
            /// </summary>
            public Dictionary<string, List<string>> SubassetDefinitions { get; } = [];

            /// <summary>
            /// Gets the subasset references.
            /// </summary>
            public Dictionary<string, Dictionary<string, int>> SubassetReferences { get; } = [];
        }

        /// <summary>
        /// Magic identifier for the file format.
        /// </summary>
        public const uint MAGIC = 0xC4CCACE8;

        /// <summary>
        /// Magic identifier for the newer file format.
        /// </summary>
        public const uint MAGIC2 = 0xC4CCACE9;

        /// <summary>
        /// Guard value.
        /// </summary>
        public const uint GUARD = 0x049A48B2;

        /// <summary>
        /// File version.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// All the assets.
        /// </summary>
        public Dictionary<string, File> Files { get; } = [];

        /// <summary>
        /// Gets the KV3 segment data, if present.
        /// </summary>
        public ValveKeyValue.KVObject? KV3Segment { get; private set; }

        /// <summary>
        /// Opens the given file, reads its contents into this instance, and closes the file before returning.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Read(fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, true);
            var magic = reader.ReadUInt32();
            Version = reader.ReadUInt32();

            if (magic == MAGIC2)
            {
                if (Version < 11 || Version > 15)
                {
                    throw new UnexpectedMagicException("Unexpected version", Version, nameof(Version));
                }
            }
            else if (magic == MAGIC)
            {
                if (Version != 9 && Version != 10)
                {
                    throw new UnexpectedMagicException("Unexpected version (old magic)", Version, nameof(Version));
                }
            }
            else
            {
                throw new UnexpectedMagicException("Given file is not tools_asset_info", magic, nameof(magic));
            }

            var fileCount = reader.ReadInt32();

            // Whether the edit info and misc string tables are stored, files without them are not supported here
            var hasEditInfoAndMiscStrings = reader.ReadUInt32();

            if (hasEditInfoAndMiscStrings != 1)
            {
                throw new UnexpectedMagicException("Unexpected", hasEditInfoAndMiscStrings, nameof(hasEditInfoAndMiscStrings));
            }

            var mods = ReadStringsBlock(reader);
            var directories = ReadStringsBlock(reader);
            var flenames = ReadStringsBlock(reader);
            var extensions = ReadStringsBlock(reader);
            var editInfoKeys = ReadStringsBlock(reader);
            var miscStrings = ReadStringsBlock(reader);
            List<string> subassetDefinitions;
            List<string> subassetValues;

            if (Version >= 12)
            {
                subassetDefinitions = ReadStringsBlock(reader);
                subassetValues = ReadStringsBlock(reader);
            }
            else
            {
                subassetDefinitions = [];
                subassetValues = [];
            }

            var path = new StringBuilder(128);

            // The top 3 bits are the asset location, which is not part of the path
            string ConstructFilePath(ulong hash)
            {
                var addonIndex = (int)((hash >> 52) & 0x1FF);
                var directoryIndex = (int)((hash >> 33) & 0x7FFFF);
                var filenameIndex = (int)((hash >> 10) & 0x7FFFFF);
                var extensionIndex = (int)(hash & 0x3FF);

                path.Clear();

                if (addonIndex != 0x1FF)
                {
                    path.Append(mods[addonIndex]);
                    path.Append('/');
                }

                if (directoryIndex != 0x7FFFF)
                {
                    path.Append(directories[directoryIndex]);
                    path.Append('/');
                }

                if (filenameIndex != 0x7FFFFF)
                {
                    path.Append(flenames[filenameIndex]);
                }

                if (extensionIndex != 0x3FF)
                {
                    path.Append('.');
                    path.Append(extensions[extensionIndex]);
                }

                return path.ToString();
            }

            string GetMiscString(int index) => index >= 0 ? miscStrings[index] : string.Empty;

            Files.EnsureCapacity(fileCount);

            var lookup = new File[fileCount];

            for (var fileId = 0; fileId < fileCount; fileId++)
            {
                var hash = reader.ReadUInt64();
                var file = new File();

                lookup[fileId] = file;
                Files[ConstructFilePath(hash)] = file;
            }

            if (Version >= 14)
            {
                // Align to 8-byte boundary
                var currentPos = reader.BaseStream.Position;
                var alignedPos = (currentPos + 7) & ~7L;

                if (currentPos < alignedPos)
                {
                    var paddingBytes = alignedPos - currentPos;

                    for (var i = 0; i < paddingBytes; i++)
                    {
                        if (reader.ReadByte() != 0)
                        {
                            throw new InvalidDataException("Alignment padding contains non-zero bytes");
                        }
                    }
                }

                var kv3magic = reader.ReadUInt32();
                reader.BaseStream.Position -= 4; // rewind

                if (BinaryKV3.IsBinaryKV3(kv3magic))
                {
                    var kv3 = new BinaryKV3(BlockType.Undefined)
                    {
                        Resource = null!
                    };
                    kv3.Read(reader);

                    KV3Segment = kv3.Data;
                }
            }

            // These blocks quite closely match RERL and REDI blocks in the individual files
            for (var fileId = 0; fileId < fileCount; fileId++)
            {
                var file = lookup[fileId];

                file.NeedsRefresh = reader.ReadBoolean();
                int count;

                for (var searchPathType = 0; searchPathType < 2; searchPathType++)
                {
                    count = reader.ReadInt32();

                    while (count-- > 0)
                    {
                        var hash = reader.ReadUInt64();

                        // The location bits of the hash always match the search path type
                        Debug.Assert((AssetLocation)(hash >> 61) == (searchPathType == 0 ? AssetLocation.Game : AssetLocation.Content));

                        // One 128-bit record: crc32 (bits 0-31), modification time (bits 32-87),
                        // file size (bits 88-126), and a runtime only marker that is always zero on disk (bit 127).
                        var packedLow = reader.ReadUInt64();
                        var packedHigh = reader.ReadUInt64();

                        Debug.Assert((packedHigh >> 63) == 0);

                        var searchPath = new File.SearchPath
                        {
                            Filename = ConstructFilePath(hash),
                            FileCRC = (uint)packedLow,
                            ModificationTimeFileTime = (long)(((packedLow >> 32) | ((packedHigh & 0xFFFFFF) << 32)) << 8),
                            FileSize = (long)((packedHigh >> 24) & 0x7F_FFFF_FFFF),
                        };

                        switch (searchPathType)
                        {
                            case 0: file.SearchPathsGameRoot.Add(searchPath); break;
                            case 1: file.SearchPathsContentRoot.Add(searchPath); break;
                            default: throw new InvalidOperationException();
                        }
                    }
                }

                if (!reader.ReadBoolean())
                {
                    continue;
                }

                file.Invalid = reader.ReadBoolean();
                file.UpToDate = reader.ReadBoolean();
                file.CompileFailed = reader.ReadBoolean();

                // m_InputDependencies
                count = reader.ReadInt32();
                file.InputDependencies.Capacity = count;

                while (count-- > 0)
                {
                    var hash = reader.ReadUInt64();
                    var fileCRC = reader.ReadUInt32();
                    var isOptional = reader.ReadBoolean();
                    var fileExists = reader.ReadBoolean();

                    file.InputDependencies.Add(new File.InputDependency
                    {
                        Filename = ConstructFilePath(hash),
                        FileCRC = fileCRC,
                        Optional = isOptional,
                        FileExists = fileExists,
                    });
                }

                // RERL
                count = reader.ReadInt32();
                file.ExternalReferences.Capacity = count;

                while (count-- > 0)
                {
                    var hash = reader.ReadUInt64();

                    file.ExternalReferences.Add(ConstructFilePath(hash));
                }

                // m_ChildResourceList
                count = reader.ReadInt32();
                file.ChildResources.Capacity = count;

                while (count-- > 0)
                {
                    var hash = reader.ReadUInt64();

                    file.ChildResources.Add(ConstructFilePath(hash));
                }

                // m_AdditionalRelatedFiles
                count = reader.ReadInt32();
                file.AdditionalRelatedFiles.Capacity = count;

                while (count-- > 0)
                {
                    var hash = reader.ReadUInt64();

                    file.AdditionalRelatedFiles.Add(ConstructFilePath(hash));
                }

                // m_SpecialDependencies
                count = reader.ReadInt32();
                file.SpecialDependencies.Capacity = count;

                while (count-- > 0)
                {
                    int compilerIdentifierId;
                    int stringId;

                    if (Version >= 11)
                    {
                        compilerIdentifierId = reader.ReadInt32();
                        stringId = reader.ReadInt32();
                    }
                    else
                    {
                        compilerIdentifierId = reader.ReadInt16();
                        stringId = reader.ReadInt16();
                    }

                    var userData = reader.ReadUInt32();
                    var fingerprint = reader.ReadUInt32();

                    file.SpecialDependencies.Add(new File.SpecialDependency
                    {
                        String = miscStrings[stringId],
                        CompilerIdentifier = miscStrings[compilerIdentifierId],
                        UserData = userData,
                        Fingerprint = fingerprint,
                    });
                }

                if (Version >= 15)
                {
                    // m_SpecialInputDependencies
                    count = reader.ReadInt32();
                    file.SpecialInputDependencies.Capacity = count;

                    while (count-- > 0)
                    {
                        var compilerIdentifierId = reader.ReadInt32();
                        var specialId = reader.ReadInt32();
                        var userDataId = reader.ReadInt32();
                        var fileHash = reader.ReadUInt64();
                        var fingerprint = reader.ReadUInt32();

                        file.SpecialInputDependencies.Add(new File.SpecialInputDependency
                        {
                            CompilerIdentifier = GetMiscString(compilerIdentifierId),
                            Special = GetMiscString(specialId),
                            UserData = GetMiscString(userDataId),
                            Filename = ConstructFilePath(fileHash),
                            Fingerprint = fingerprint,
                        });
                    }
                }

                // m_SearchableUserData
                count = reader.ReadInt32();
                file.SearchableUserData.EnsureCapacity(count);

                while (count-- > 0)
                {
                    var keyId = reader.ReadUInt16();
                    var type = reader.ReadByte();
                    object? value = null;

                    if (type == 2)
                    {
                        int assetInfoValue;

                        if (Version >= 11)
                        {
                            assetInfoValue = reader.ReadInt32();
                        }
                        else
                        {
                            assetInfoValue = reader.ReadInt16();
                        }

                        value = GetMiscString(assetInfoValue);
                    }
                    else if (type == 1)
                    {
                        var floatValue = reader.ReadSingle();
                        value = floatValue;
                    }
                    else
                    {
                        var intValue = reader.ReadInt32();
                        value = intValue;
                    }

                    // Possible to have duplicates here!
                    file.SearchableUserData[editInfoKeys[keyId]] = value;
                }

                // m_AdditionalInputDependencies
                count = reader.ReadInt32();
                file.AdditionalInputDependencies.Capacity = count;

                while (count-- > 0)
                {
                    var hash = reader.ReadUInt64();
                    var fileCRC = reader.ReadUInt32();
                    var isOptional = reader.ReadBoolean();
                    var fileExists = reader.ReadBoolean();

                    file.AdditionalInputDependencies.Add(new File.InputDependency
                    {
                        Filename = ConstructFilePath(hash),
                        FileCRC = fileCRC,
                        Optional = isOptional,
                        FileExists = fileExists,
                    });
                }

                if (Version >= 12)
                {
                    // m_SubassetDefinitions
                    count = reader.ReadInt32();
                    file.SubassetDefinitions.EnsureCapacity(count);

                    while (count-- > 0)
                    {
                        var hash = reader.ReadInt32();
                        var definition = hash >> 24;
                        var value = hash & 0xFFFFFF;

                        var definitionKey = subassetDefinitions[definition];

                        if (!file.SubassetDefinitions.TryGetValue(definitionKey, out var list))
                        {
                            list = [];
                            file.SubassetDefinitions[definitionKey] = list;
                        }

                        list.Add(subassetValues[value]);
                    }

                    // m_SubassetReferences
                    count = reader.ReadInt32();
                    file.SubassetReferences.EnsureCapacity(count);

                    while (count-- > 0)
                    {
                        var hash = reader.ReadInt32();
                        var definition = hash >> 24;
                        var value = hash & 0xFFFFFF;
                        var references = reader.ReadUInt16();

                        var definitionKey = subassetDefinitions[definition];

                        if (!file.SubassetReferences.TryGetValue(definitionKey, out var list))
                        {
                            list = [];
                            file.SubassetReferences[definitionKey] = list;
                        }

                        list[subassetValues[value]] = references;
                    }
                }

                if (Version >= 13)
                {
                    // m_WeakReferenceList
                    count = reader.ReadInt32();
                    file.WeakReferences.Capacity = count;

                    while (count-- > 0)
                    {
                        var hash = reader.ReadUInt64();

                        file.WeakReferences.Add(ConstructFilePath(hash));
                    }
                }
            }

            if (Version >= 10)
            {
                var guard = reader.ReadUInt32();
                UnexpectedMagicException.Assert(guard == GUARD, guard);
            }
        }

        private static List<string> ReadStringsBlock(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var output = new List<string>(count);

            for (uint i = 0; i < count; i++)
            {
                output.Add(reader.ReadNullTermString(Encoding.UTF8));
            }

            return output;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, Files, nameof(ToolsAssetInfo));
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
