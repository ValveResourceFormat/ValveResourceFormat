using System;
using System.CodeDom.Compiler;
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
        public enum REDIStruct
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

        public Dictionary<REDIStruct, ResourceEditInfoStructs.REDIBlock> Structs;

        public ResourceEditInfo()
        {
            Structs = new Dictionary<REDIStruct, ResourceEditInfoStructs.REDIBlock>();
        }

        public override BlockType GetChar()
        {
            return BlockType.REDI;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = REDIStruct.InputDependencies; i < REDIStruct.End; i++)
            {
                var block = ConstructStruct(i);

                block.Offset = (uint)reader.BaseStream.Position + reader.ReadUInt32();
                block.Size = reader.ReadUInt32();

                Structs.Add(i, block);
            }

            foreach (var block in Structs)
            {
                block.Value.Read(reader, resource);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("ResourceEditInfoBlock_t");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var dep in Structs)
            {
                dep.Value.WriteText(writer);
            }

            writer.Indent--;
            writer.WriteLine("}");
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
