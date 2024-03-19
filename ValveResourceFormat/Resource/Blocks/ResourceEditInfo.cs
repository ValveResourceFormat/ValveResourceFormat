using System.IO;
using System.Text;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t.
    /// </summary>
    public class ResourceEditInfo : Block
    {
        public override BlockType Type => BlockType.REDI;

        public List<InputDependency> InputDependencies { get; } = [];
        public List<InputDependency> AdditionalInputDependencies { get; } = [];
        public List<ArgumentDependency> ArgumentDependencies { get; } = [];
        public List<SpecialDependency> SpecialDependencies { get; } = [];
        public List<AdditionalRelatedFile> AdditionalRelatedFiles { get; } = [];
        public List<string> ChildResourceList { get; } = [];
        public KVObject SearchableUserData { get; } = new("m_SearchableUserData"); // Maybe these should be split..

        public override void Read(BinaryReader reader, Resource resource)
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

            void ReadItems<T>(List<T> list, Func<T> constructor)
            {
                var count = AdvanceGetCount();
                list.EnsureCapacity(count);

                for (var i = 0; i < count; i++)
                {
                    var item = constructor.Invoke();
                    list.Add(item);
                }
            }

            void ReadKeyValues<T>(KVObject kvObject, KVType valueType, Func<T> valueReader)
            {
                var count = AdvanceGetCount();
                kvObject.Properties.EnsureCapacity(kvObject.Properties.Count + count);

                for (var i = 0; i < count; i++)
                {
                    var key = reader.ReadOffsetString(Encoding.UTF8);
                    var value = valueReader.Invoke();
                    kvObject.AddProperty(key, new KVValue(valueType, value));
                }
            }

            ReadItems(InputDependencies, () => new InputDependency(reader));
            ReadItems(AdditionalInputDependencies, () => new InputDependency(reader));
            ReadItems(ArgumentDependencies, () => new ArgumentDependency(reader));
            ReadItems(SpecialDependencies, () => new SpecialDependency(reader));

            var customDependencies = AdvanceGetCount();
            if (customDependencies > 0)
            {
                throw new NotImplementedException("CustomDependencies in REDI are not handled.\n" +
                    "Please report this on https://github.com/ValveResourceFormat/ValveResourceFormat and provide the file that caused this exception.");
            }

            ReadItems(AdditionalRelatedFiles, () => new AdditionalRelatedFile(reader));
            ReadItems(ChildResourceList, () =>
            {
                var id = reader.ReadUInt64();
                var name = reader.ReadOffsetString(Encoding.UTF8);
                var unknown = reader.ReadInt32();
                return name; // Ignoring 'id' to match RED2
            });

            ReadKeyValues(SearchableUserData, KVType.INT64, () => (long)reader.ReadInt32());
            ReadKeyValues(SearchableUserData, KVType.FLOAT, () => (double)reader.ReadSingle());
            ReadKeyValues(SearchableUserData, KVType.STRING, () => reader.ReadOffsetString(Encoding.UTF8));
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            using var ms = new MemoryStream();
            var serializer = ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues1Text);
            serializer.Serialize(ms, this, "ResourceEditInfo");

            writer.Write(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }
}
