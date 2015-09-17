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
                public List<byte> Indirections { get; private set; }
                public uint TypeData { get; set; }
                public short Type { get; set; } // TODO: make this an enum?

                public Field()
                {
                    Indirections = new List<byte>();
                }

                public override string ToString()
                {
                    var str = new StringBuilder();

                    str.AppendLine("\t\t\t\t\tCResourceDiskStructField");
                    str.AppendLine("\t\t\t\t\t{");
                    str.AppendFormat("\t\t\t\t\t\tCResourceString m_pFieldName = \"{0}\"\n", FieldName);
                    str.AppendFormat("\t\t\t\t\t\tint16 m_nCount = {0}\n", Count);
                    str.AppendFormat("\t\t\t\t\t\tint16 m_nOnDiskOffset = {0}\n", OnDiskOffset);

                    str.AppendFormat("\t\t\t\t\t\tuint8[{0}] m_Indirection =\n", Indirections.Count);
                    str.AppendLine("\t\t\t\t\t\t[");

                    foreach (var dep in Indirections)
                    {
                        str.AppendFormat("\t\t\t\t\t\t\t{0:D2}\n", dep);
                    }

                    str.AppendLine("\n\t\t\t\t\t\t]");

                    str.AppendFormat("\t\t\t\t\t\tuint32 m_nTypeData = 0x{0:X8}\n", TypeData);
                    str.AppendFormat("\t\t\t\t\t\tint16 m_nType = {0}\n", Type);
                    str.AppendLine("\t\t\t\t\t}");

                    return str.ToString();
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
            public byte StructFlags { get; set; }
            public List<Field> FieldIntrospection { get; private set; }

            public ResourceDiskStruct()
            {
                FieldIntrospection = new List<Field>();
            }

            public override string ToString()
            {
                var str = new StringBuilder();

                str.AppendLine("\t\t\tCResourceDiskStruct");
                str.AppendLine("\t\t\t{");
                str.AppendFormat("\t\t\t\tuint32 m_nIntrospectionVersion = 0x{0:X8}\n", IntrospectionVersion);
                str.AppendFormat("\t\t\t\tuint32 m_nId = 0x{0:X8}\n", Id);
                str.AppendFormat("\t\t\t\tCResourceString m_pName = \"{0}\"\n", Name);
                str.AppendFormat("\t\t\t\tuint32 m_nDiskCrc = 0x{0:X8}\n", DiskCrc);
                str.AppendFormat("\t\t\t\tint32 m_nUserVersion = {0}\n", UserVersion);
                str.AppendFormat("\t\t\t\tuint16 m_nDiskSize = 0x{0:X4}\n", DiskSize);
                str.AppendFormat("\t\t\t\tuint16 m_nAlignment = 0x{0:X4}\n", Alignment);
                str.AppendFormat("\t\t\t\tuint32 m_nBaseStructId = 0x{0:X8}\n", BaseStructId);

                str.AppendFormat("\t\t\t\tStruct m_FieldIntrospection[{0}] = \n", FieldIntrospection.Count);
                str.AppendLine("\t\t\t\t[");

                foreach (var dep in FieldIntrospection)
                {
                    str.Append(dep.ToString());
                }

                str.AppendLine("\t\t\t\t]");
                str.AppendFormat("\t\t\t\tuint8 m_nStructFlags = 0x{0:X2}\n", StructFlags);
                str.AppendLine("\t\t\t}");

                return str.ToString();
            }
        }

        public class ResourceDiskEnum
        {
            public class Value
            {
                public string EnumValueName { get; set; }
                public int EnumValue { get; set; }

                public override string ToString()
                {
                    var str = new StringBuilder();

                    str.AppendLine("\t\t\t\t\tCResourceDiskEnumValue");
                    str.AppendLine("\t\t\t\t\t{");
                    str.AppendFormat("\t\t\t\t\t\tCResourceString m_pEnumValueName = \"{0}\"\n", EnumValueName);
                    str.AppendFormat("\t\t\t\t\t\tint32 m_nEnumValue = {0}\n", EnumValue);
                    str.AppendLine("\t\t\t\t\t}");

                    return str.ToString();
                }
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

            public override string ToString()
            {
                var str = new StringBuilder();

                str.AppendLine("\t\t\tCResourceDiskEnum");
                str.AppendLine("\t\t\t{");
                str.AppendFormat("\t\t\t\tuint32 m_nIntrospectionVersion = 0x{0:X8}\n", IntrospectionVersion);
                str.AppendFormat("\t\t\t\tuint32 m_nId = 0x{0:X8}\n", Id);
                str.AppendFormat("\t\t\t\tCResourceString m_pName = \"{0}\"\n", Name);
                str.AppendFormat("\t\t\t\tuint32 m_nDiskCrc = 0x{0:X8}\n", DiskCrc);
                str.AppendFormat("\t\t\t\tint32 m_nUserVersion = {0}\n", UserVersion);

                str.AppendFormat("\t\t\t\tStruct m_EnumValueIntrospection[{0}] = \n", EnumValueIntrospection.Count);
                str.AppendLine("\t\t\t\t[");

                foreach (var dep in EnumValueIntrospection)
                {
                    str.Append(dep.ToString());
                }

                str.AppendLine("\t\t\t\t]");
                str.AppendLine("\t\t\t}");

                return str.ToString();
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
            ReadStructs(reader);
            ReadEnums(reader);
        }

        private void ReadStructs(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            IntrospectionVersion = reader.ReadUInt32();

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
                        field.Indirections.Add(reader.ReadByte());
                    }

                    reader.BaseStream.Position = prev2;

                    field.TypeData = reader.ReadUInt32();
                    field.Type = reader.ReadInt16();

                    reader.ReadBytes(2); // TODO: ????

                    diskStruct.FieldIntrospection.Add(field);
                }

                reader.BaseStream.Position = prev;

                diskStruct.StructFlags = reader.ReadByte();
                
                reader.ReadBytes(3); // TODO: ????

                ReferencedStructs.Add(diskStruct);
            }
        }

        private void ReadEnums(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset + 12; // skip 3 ints

            var entriesOffset = reader.ReadUInt32();
            var entriesCount = reader.ReadUInt32();

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

        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendLine("CResourceIntrospectionManifest");
            str.AppendLine("\t{");

            str.AppendFormat("\t\tuint32 m_nIntrospectionVersion = 0x{0:x8}\n", IntrospectionVersion);
            str.AppendFormat("\t\tStruct m_ReferencedStructs[{0}] = \n", ReferencedStructs.Count);
            str.AppendLine("\t\t[");

            foreach (var dep in ReferencedStructs)
            {
                str.Append(dep.ToString());
            }

            str.AppendLine("\t\t]");

            str.AppendFormat("\t\tStruct m_ReferencedEnums[{0}] = \n", ReferencedEnums.Count);
            str.AppendLine("\t\t[");

            foreach (var dep in ReferencedEnums)
            {
                str.Append(dep.ToString());
            }

            str.AppendLine("\t\t]");

            str.AppendLine("\t}");

            return str.ToString();
        }
    }
}
