using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class KeyValuesOrNTRO : ResourceData
    {
        private readonly string IntrospectionStructName;
        private readonly BlockType KVBlockType;
        public override BlockType Type => KVBlockType;

        protected Resource Resource { get; private set; }
        public IKeyValueCollection Data { get; private set; }
        public bool UpgradeToKV3 { get; private set; }

        private ResourceData BackingData;

        public KeyValuesOrNTRO()
        {
            KVBlockType = BlockType.DATA;
        }

        public KeyValuesOrNTRO(BlockType type, string introspectionStructName, bool upgradeToKV3 = false)
        {
            KVBlockType = type;
            IntrospectionStructName = introspectionStructName;
            UpgradeToKV3 = upgradeToKV3;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            Resource = resource;

            if (!resource.ContainsBlockType(BlockType.NTRO))
            {
                var kv3 = new BinaryKV3(KVBlockType)
                {
                    Offset = Offset,
                    Size = Size,
                };
                kv3.Read(reader, resource);
                Data = kv3.Data;
                BackingData = kv3;
            }
            else
            {
                var ntro = new NTRO
                {
                    StructName = IntrospectionStructName,
                    Offset = Offset,
                    Size = Size,
                };
                ntro.Read(reader, resource);
                Data = UpgradeToKV3 ? ntro.Output.ToKVObject() : ntro.Output;
                BackingData = ntro;
            }
        }

        public override string ToString()
        {
            if (BackingData is BinaryKV3 dataKv3)
            {
                return dataKv3.GetKV3File().ToString();
            }

            return BackingData.ToString();
        }
    }
}
