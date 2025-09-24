using System.IO;
using System.Text;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t.
    /// </summary>
    public class ResourceEditInfo : RawBinary
    {
        // Serialize legacy REDI info by copying raw data from the original resource beacuse we have no plans to support NTRO serialization
        public override BlockType Type => BlockType.REDI;

        public List<InputDependency> InputDependencies { get; } = [];
        public List<InputDependency> AdditionalInputDependencies { get; } = [];
        public List<ArgumentDependency> ArgumentDependencies { get; } = [];
        public List<SpecialDependency> SpecialDependencies { get; } = [];
        public List<AdditionalRelatedFile> AdditionalRelatedFiles { get; } = [];
        public List<string> ChildResourceList { get; } = [];
        public KVObject SearchableUserData { get; } = new("m_SearchableUserData"); // Maybe these should be split..

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
                kvObject.Properties.EnsureCapacity(kvObject.Properties.Count + count);

                for (var i = 0; i < count; i++)
                {
                    var key = reader.ReadOffsetString(Encoding.UTF8);
                    var value = valueReader.Invoke(reader);

                    // Note: we may override existing keys
                    kvObject.Properties[key] = new KVValue(value);
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
            ReadItems(ChildResourceList, static (reader) =>
            {
                var id = reader.ReadUInt64();
                var name = reader.ReadOffsetString(Encoding.UTF8);
                var unknown = reader.ReadInt32();
                return name; // Ignoring 'id' to match RED2
            });

            ReadKeyValues(SearchableUserData, static (reader) => (long)reader.ReadInt32());
            ReadKeyValues(SearchableUserData, static (reader) => (double)reader.ReadSingle());
            ReadKeyValues(SearchableUserData, static (reader) => reader.ReadOffsetString(Encoding.UTF8));
        }

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
                SearchableUserData,
            };

            serializer.Serialize(ms, serializedProps, "ResourceEditInfo");

            writer.Write(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }
}
