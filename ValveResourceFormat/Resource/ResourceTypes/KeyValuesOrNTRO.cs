using System;
using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class KeyValuesOrNTRO : ResourceData
    {
        protected Resource Resource { get; private set; }
        public IKeyValueCollection Data { get; private set; }

        private ResourceData BackingData;

        public override void Read(BinaryReader reader, Resource resource)
        {
            Resource = resource;

            if (!resource.ContainsBlockType(BlockType.NTRO))
            {
                var kv3 = new BinaryKV3
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
                    Offset = Offset,
                    Size = Size,
                };
                ntro.Read(reader, resource);
                Data = ntro.Output;
                BackingData = ntro;
            }
        }

        public override string ToString()
        {
            return BackingData.ToString();
        }
    }
}
