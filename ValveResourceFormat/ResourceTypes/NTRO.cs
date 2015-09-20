using System;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class NTRO : ResourceData
    {
        public override void Read(BinaryReader reader, Resource resource)
        {
            //reader.BaseStream.Position = this.Offset;

            foreach (var refStruct in resource.IntrospectionManifest.ReferencedStructs)
            {
                foreach (var field in refStruct.FieldIntrospection)
                {
                    reader.BaseStream.Position = this.Offset + field.OnDiskOffset;

                    var offset = this.Offset + reader.ReadUInt32();
                    var size = reader.ReadUInt32();

                    Console.WriteLine(field.FieldName + " " + field.Type + " size: " + size);

                    reader.BaseStream.Position = offset + field.OnDiskOffset;

                    switch (field.Type)
                    {
                        case DataType.Boolean:
                            Console.WriteLine(reader.ReadByte());
                            break;

                        case DataType.String4:
                        case DataType.String:
                            Console.WriteLine(reader.ReadNullTermString(Encoding.UTF8));
                            break;
                    }
                }

                break;
            }
        }

        public override string ToString()
        {
            return "yolo";
        }
    }
}
