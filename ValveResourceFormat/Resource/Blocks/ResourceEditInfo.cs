using System.Collections.Generic;
using System.IO;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t.
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

            End,
        }

        public Dictionary<REDIStruct, REDIBlock> Structs { get; private set; }

        public ResourceEditInfo()
        {
            Structs = new Dictionary<REDIStruct, REDIBlock>();
        }

        public override BlockType GetChar()
        {
            return BlockType.REDI;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

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

        private static REDIBlock ConstructStruct(REDIStruct id)
        {
            switch (id)
            {
                case REDIStruct.InputDependencies:
                    return new InputDependencies();
                case REDIStruct.AdditionalInputDependencies:
                    return new AdditionalInputDependencies();
                case REDIStruct.ArgumentDependencies:
                    return new ArgumentDependencies();
                case REDIStruct.SpecialDependencies:
                    return new SpecialDependencies();
                case REDIStruct.CustomDependencies:
                    return new CustomDependencies();
                case REDIStruct.AdditionalRelatedFiles:
                    return new AdditionalRelatedFiles();
                case REDIStruct.ChildResourceList:
                    return new ChildResourceList();
                case REDIStruct.ExtraIntData:
                    return new ExtraIntData();
                case REDIStruct.ExtraFloatData:
                    return new ExtraFloatData();
                case REDIStruct.ExtraStringData:
                    return new ExtraStringData();
            }

            throw new InvalidDataException("Unknown struct in REDI block.");
        }
    }
}
