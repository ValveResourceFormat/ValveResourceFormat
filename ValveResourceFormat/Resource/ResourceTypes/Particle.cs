using System.IO;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class Particle : NTRO
    {
        private const int SIGNATURE = 55987030; // "VKV\x03" aka valve keyvalue, version 3

        public override void Read(BinaryReader reader, Resource resource)
        {
            if (resource.IntrospectionManifest == null)
            {
                var block = new ResourceIntrospectionManifest.ResourceDiskStruct();

                var field = new ResourceIntrospectionManifest.ResourceDiskStruct.Field
                {
                    FieldName = "m_Signature",
                    Count = 1,
                    Type = DataType.Int32,
                };
                block.FieldIntrospection.Add(field);

                field = new ResourceIntrospectionManifest.ResourceDiskStruct.Field
                {
                    FieldName = "m_Encoding",
                    Count = 4,
                    OnDiskOffset = 4,
                    Type = DataType.Boolean,
                };
                block.FieldIntrospection.Add(field);

                field = new ResourceIntrospectionManifest.ResourceDiskStruct.Field
                {
                    FieldName = "m_Format",
                    Count = 4,
                    OnDiskOffset = 20,
                    Type = DataType.Boolean,
                };
                block.FieldIntrospection.Add(field);

                resource.Blocks[BlockType.NTRO] = new ResourceIntrospectionManifest();
                resource.IntrospectionManifest.ReferencedStructs.Add(block);
            }

            base.Read(reader, resource);

            reader.BaseStream.Position = Offset;

            // TODO: Use parsed NTRO data
            if (reader.ReadUInt32() != SIGNATURE)
            {
                throw new InvalidDataException("Wrong signature.");
            }

            reader.BaseStream.Position += 32; // encoding + format (guids?)

            // TODO
        }

        public override string ToString()
        {
            return "particle";
        }
    }
}
