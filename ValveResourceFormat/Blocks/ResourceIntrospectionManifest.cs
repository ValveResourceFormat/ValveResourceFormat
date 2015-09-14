using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "NTRO" block. CResourceIntrospectionManifest
    /// </summary>
    public class ResourceIntrospectionManifest : Block
    {
        public class ResourceDiskStruct
        {
            public class Field
            {
                public string FieldName { get; set; }
                public short Count { get; set; }
                public short OnDiskOffset { get; set; }
                public List<sbyte> Indirections { get; private set; }
                public uint TypeData { get; set; }
                public short Type { get; set; } // TODO: make this an enum?

                public Field()
                {
                    Indirections = new List<sbyte>();
                }
            }

            public uint IntrospectionVersion { get; set; }
            public uint Id { get; set; }
            public string Name { get; set; }
            public uint DiskCrc { get; set; }
            public int UserVersion { get; set; }
            public ushort DiskSize { get; set; }
            public ushort Alignment { get; set; }
            public uint BaseStructId { get; set; }
            public sbyte StructFlags { get; set; }
            public List<Field> FieldIntrospection { get; private set; }

            public ResourceDiskStruct()
            {
                FieldIntrospection = new List<Field>();
            }
        }

        public class ResourceDiskEnum
        {
            public class Value
            {
                public string EnumValueName { get; set; }
                public int EnumValue { get; set; }
            }

            public uint IntrospectionVersion { get; set; }
            public uint Id { get; set; }
            public string Name { get; set; }
            public uint DiskCrc { get; set; }
            public int UserVersion { get; set; }
            public List<Value> EnumValueIntrospection { get; private set; }

            public ResourceDiskEnum()
            {
                EnumValueIntrospection = new List<Value>();
            }
        }

        public uint IntrospectionVersion { get; private set; }

        public List<ResourceDiskStruct> ReferencedStructs;
        public List<ResourceDiskEnum> ReferencedEnums;

        public ResourceIntrospectionManifest()
        {
            ReferencedStructs = new List<ResourceDiskStruct>();
            ReferencedEnums = new List<ResourceDiskEnum>();
        }

        public override BlockType GetChar()
        {
            return BlockType.NTRO;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            IntrospectionVersion = reader.ReadUInt32();

            Console.WriteLine("m_nIntrospectionVersion = 0x{0:x8}", IntrospectionVersion);

            var entriesOffset = reader.ReadUInt32();
            var entriesCount = reader.ReadUInt32();

            reader.BaseStream.Position += entriesOffset - 8; // offset minus 2 ints we just read

            while (entriesCount-- > 0)
            {
                var diskStruct = new ResourceDiskStruct();
                diskStruct.IntrospectionVersion = reader.ReadUInt32();
                diskStruct.Id = reader.ReadUInt32();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                diskStruct.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                diskStruct.DiskCrc = reader.ReadUInt32();
                diskStruct.UserVersion = reader.ReadInt32();
                diskStruct.DiskSize = reader.ReadUInt16();
                diskStruct.Alignment = reader.ReadUInt16();
                diskStruct.BaseStructId = reader.ReadUInt32();

                var fieldsOffset = reader.ReadUInt32();
                var fieldsSize = reader.ReadUInt32();

                // jump to fields
                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += fieldsOffset - 8; // offset minus 2 ints we just read

                while (fieldsSize-- > 0)
                {
                    var field = new ResourceDiskStruct.Field();

                    var prev2 = reader.BaseStream.Position;
                    reader.BaseStream.Position += reader.ReadUInt32();
                    field.FieldName = reader.ReadNullTermString(Encoding.UTF8);
                    reader.BaseStream.Position = prev2 + 4;

                    field.Count = reader.ReadInt16();
                    field.OnDiskOffset = reader.ReadInt16();

                    var indirectionOffset = reader.ReadUInt32();
                    var indirectionSize = reader.ReadUInt32();

                    // jump to indirections
                    prev2 = reader.BaseStream.Position;
                    reader.BaseStream.Position += indirectionOffset - 8; // offset minus 2 ints we just read

                    while (indirectionSize-- > 0)
                    {
                        field.Indirections.Add(reader.ReadSByte());
                    }

                    reader.BaseStream.Position = prev2;

                    field.TypeData = reader.ReadUInt32();
                    field.Type = reader.ReadInt16();

                    reader.ReadBytes(2); // TODO: ????

                    diskStruct.FieldIntrospection.Add(field);
                }

                reader.BaseStream.Position = prev;

                diskStruct.StructFlags = reader.ReadSByte();
                
                reader.ReadBytes(3); // TODO: ????

                ReferencedStructs.Add(diskStruct);
            }

            reader.BaseStream.Position = this.Offset + 12; // skip 3 ints

            entriesOffset = reader.ReadUInt32();
            entriesCount = reader.ReadUInt32();

            reader.BaseStream.Position += entriesOffset - 8; // offset minus 2 ints we just read

            while (entriesCount-- > 0)
            {
                var diskEnum = new ResourceDiskEnum();
                diskEnum.IntrospectionVersion = reader.ReadUInt32();
                diskEnum.Id = reader.ReadUInt32();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                diskEnum.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                diskEnum.DiskCrc = reader.ReadUInt32();
                diskEnum.UserVersion = reader.ReadInt32();

                var fieldsOffset = reader.ReadUInt32();
                var fieldsSize = reader.ReadUInt32();

                // jump to fields
                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += fieldsOffset - 8; // offset minus 2 ints we just read

                while (fieldsSize-- > 0)
                {
                    var field = new ResourceDiskEnum.Value();

                    var prev2 = reader.BaseStream.Position;
                    reader.BaseStream.Position += reader.ReadUInt32();
                    field.EnumValueName = reader.ReadNullTermString(Encoding.UTF8);
                    reader.BaseStream.Position = prev2 + 4;

                    field.EnumValue = reader.ReadInt32();

                    diskEnum.EnumValueIntrospection.Add(field);
                }

                reader.BaseStream.Position = prev;

                ReferencedEnums.Add(diskEnum);
            }
        }
    }
}
