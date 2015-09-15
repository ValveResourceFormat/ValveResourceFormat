using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t
    /// </summary>
    public class ResourceEditInfo : Block
    {
        /// <summary>
        /// This is not a real Valve enum, it's just the order they appear in.
        /// </summary>
        enum REDIStruct
        {
            InputDependencies,
            AdditionalInputDependencies,
            ArgumentDependencies,
            SpecialDependencies,
            CustomDependencies,
            AdditionalRelatedFiles,
            ChildResourceList,
            ExtraIntData,
            ExtraFloatData,
            ExtraStringData,

            End
        }

        public List<ResourceEditInfoStructs.REDIBlock> Structs;

        public ResourceEditInfo()
        {
            Structs = new List<ResourceEditInfoStructs.REDIBlock>();
        }

        public override BlockType GetChar()
        {
            return BlockType.REDI;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = REDIStruct.InputDependencies; i < REDIStruct.End; i++)
            {
                var block = ConstructStruct(i);

                block.Offset = (uint)reader.BaseStream.Position + reader.ReadUInt32();
                block.Size = reader.ReadUInt32();

                Structs.Add(block);
            }

            foreach (var block in Structs)
            {
                block.Read(reader);
            }
        }

        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendLine("ResourceEditInfoBlock_t");
            str.AppendLine("\t{");

            foreach (var dep in Structs)
            {
                str.Append(dep.ToStringIndent("\t\t"));
            }

            str.AppendLine("\t}");

            return str.ToString();
        }

        static ResourceEditInfoStructs.REDIBlock ConstructStruct(REDIStruct id)
        {
            switch (id)
            {
                case REDIStruct.InputDependencies:
                    return new ResourceEditInfoStructs.InputDependencies();
                case REDIStruct.AdditionalInputDependencies:
                    return new ResourceEditInfoStructs.AdditionalInputDependencies();
                case REDIStruct.ArgumentDependencies:
                    return new ResourceEditInfoStructs.ArgumentDependencies();
                case REDIStruct.SpecialDependencies:
                    return new ResourceEditInfoStructs.SpecialDependencies();
                case REDIStruct.CustomDependencies:
                    return new ResourceEditInfoStructs.CustomDependencies();
                case REDIStruct.AdditionalRelatedFiles:
                    return new ResourceEditInfoStructs.AdditionalRelatedFiles();
                case REDIStruct.ChildResourceList:
                    return new ResourceEditInfoStructs.ChildResourceList();
                case REDIStruct.ExtraIntData:
                    return new ResourceEditInfoStructs.ExtraIntData();
                case REDIStruct.ExtraFloatData:
                    return new ResourceEditInfoStructs.ExtraFloatData();
                case REDIStruct.ExtraStringData:
                    return new ResourceEditInfoStructs.ExtraStringData();
            }

            throw new InvalidDataException("Unknown struct in REDI block.");
        }
    }
}
