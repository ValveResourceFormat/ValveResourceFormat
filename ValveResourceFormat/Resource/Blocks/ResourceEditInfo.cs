using System.IO;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t.
    /// </summary>
    public class ResourceEditInfo : Block
    {
        public override BlockType Type => BlockType.REDI;

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
            Structs = [];
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
            return id switch
            {
                REDIStruct.InputDependencies => new InputDependencies(),
                REDIStruct.AdditionalInputDependencies => new AdditionalInputDependencies(),
                REDIStruct.ArgumentDependencies => new ArgumentDependencies(),
                REDIStruct.SpecialDependencies => new SpecialDependencies(),
                REDIStruct.CustomDependencies => new CustomDependencies(),
                REDIStruct.AdditionalRelatedFiles => new AdditionalRelatedFiles(),
                REDIStruct.ChildResourceList => new ChildResourceList(),
                REDIStruct.ExtraIntData => new ExtraIntData(),
                REDIStruct.ExtraFloatData => new ExtraFloatData(),
                REDIStruct.ExtraStringData => new ExtraStringData(),
                _ => throw new InvalidDataException($"Unknown struct in REDI block: {id}"),
            };
        }
    }
}
