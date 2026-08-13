using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t.
    /// </summary>
    public class ResourceEditInfo : RawBinary
    {
        // Serialize legacy REDI info by copying raw data from the original resource because we have no plans to support NTRO serialization
        /// <inheritdoc/>
        public override BlockType Type => BlockType.REDI;

        /// <summary>
        /// Gets the list of input dependencies.
        /// </summary>
        public List<InputDependency> InputDependencies { get; } = [];

        /// <summary>
        /// Gets the list of additional input dependencies.
        /// </summary>
        public List<InputDependency> AdditionalInputDependencies { get; } = [];

        /// <summary>
        /// Gets the list of argument dependencies.
        /// </summary>
        public List<ArgumentDependency> ArgumentDependencies { get; } = [];

        /// <summary>
        /// Gets the list of special dependencies.
        /// </summary>
        public List<SpecialDependency> SpecialDependencies { get; } = [];

        /// <summary>
        /// Gets the list of additional related files.
        /// </summary>
        public List<AdditionalRelatedFile> AdditionalRelatedFiles { get; } = [];

        /// <summary>
        /// Gets the list of child resources.
        /// </summary>
        public List<string> ChildResourceList { get; } = [];

        /// <summary>
        /// Gets the ids of the child resources, matching the order of <see cref="ChildResourceList"/>.
        /// </summary>
        /// <remarks>Only "REDI" blocks store these ids, it is empty for "RED2".</remarks>
        public List<ulong> ChildResourceIds { get; } = [];

        /// <summary>
        /// Gets the searchable user data.
        /// </summary>
        public KVObject SearchableUserData { get; } = KVObject.Collection();

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            var subBlock = 0;

            int AdvanceGetCount()
            {
                reader.BaseStream.Position = Offset + (subBlock * 8);

                var offset = reader.ReadUInt32();
                var count = reader.ReadUInt32();

                reader.BaseStream.Position = Offset + (subBlock * 8) + offset;
                subBlock++;
                return (int)count;
            }

            void ReadItems<T>(List<T> list, Func<BinaryReader, T> constructor)
            {
                var count = AdvanceGetCount();
                list.EnsureCapacity(count);

                for (var i = 0; i < count; i++)
                {
                    var item = constructor.Invoke(reader);
                    list.Add(item);
                }
            }

            void ReadKeyValues<T>(KVObject kvObject, Func<BinaryReader, T> valueReader)
            {
                var count = AdvanceGetCount();
                for (var i = 0; i < count; i++)
                {
                    var key = reader.ReadOffsetString(Encoding.UTF8);
                    var value = valueReader.Invoke(reader);

                    // Note: we may override existing keys
                    KVObject kvValue = value switch
                    {
                        string s => s,
                        long l => l,
                        double d => d,
                        float f => f,
                        int n => n,
                        _ => value!.ToString()!,
                    };
                    kvObject[key] = kvValue;
                }
            }

            ReadItems(InputDependencies, static (reader) => new InputDependency(reader));
            ReadItems(AdditionalInputDependencies, static (reader) => new InputDependency(reader));
            ReadItems(ArgumentDependencies, static (reader) => new ArgumentDependency(reader));
            ReadItems(SpecialDependencies, static (reader) => new SpecialDependency(reader));

            var customDependencies = AdvanceGetCount();
            if (customDependencies > 0)
            {
                throw new NotImplementedException("CustomDependencies in REDI are not handled.\n" +
                    "Please report this on https://github.com/ValveResourceFormat/ValveResourceFormat and provide the file that caused this exception.");
            }

            ReadItems(AdditionalRelatedFiles, static (reader) => new AdditionalRelatedFile(reader));
            var childResourceCount = AdvanceGetCount();
            ChildResourceList.EnsureCapacity(childResourceCount);
            ChildResourceIds.EnsureCapacity(childResourceCount);

            for (var i = 0; i < childResourceCount; i++)
            {
                ChildResourceIds.Add(reader.ReadUInt64());
                ChildResourceList.Add(reader.ReadOffsetString(Encoding.UTF8));
                reader.ReadInt32(); // Trailing padding, the uint64 id aligns the struct to 16 bytes
            }

            ReadKeyValues(SearchableUserData, static (reader) => (long)reader.ReadInt32());
            ReadKeyValues(SearchableUserData, static (reader) => (double)reader.ReadSingle());
            ReadKeyValues(SearchableUserData, static (reader) => reader.ReadOffsetString(Encoding.UTF8));
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            using var ms = new MemoryStream();
            var serializer = ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues1Text);
            var serializedProps = new
            {
                InputDependencies,
                AdditionalInputDependencies,
                ArgumentDependencies,
                SpecialDependencies,
                AdditionalRelatedFiles,
                ChildResourceList,
                SearchableUserData = SearchableUserData.Select(c => new { c.Key, Value = c.Value.ToString() ?? string.Empty }),
            };

            serializer.Serialize(ms, serializedProps, "ResourceEditInfo");

            writer.Write(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }
}
