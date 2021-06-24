using System;
using System.IO;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RED2" block. CResourceEditInfo.
    /// </summary>
    public class ResourceEditInfo2 : ResourceEditInfo
    {
        public override BlockType Type => BlockType.RED2;

        private BinaryKV3 BackingData;

        public ResourceEditInfo2()
        {
            //
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            var kv3 = new BinaryKV3
            {
                Offset = Offset,
                Size = Size,
            };
            kv3.Read(reader, resource);
            BackingData = kv3;

            foreach (var kv in kv3.Data)
            {
                // TODO: Structs?
                //var structType = ConstructStruct(kv.Key);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            BackingData.WriteText(writer);
        }

        private static REDIBlock ConstructStruct(string name)
        {
            return name switch
            {
                "m_InputDependencies" => new InputDependencies(),
                "m_AdditionalInputDependencies" => new AdditionalInputDependencies(),
                "m_ArgumentDependencies" => new ArgumentDependencies(),
                "m_SpecialDependencies" => new SpecialDependencies(),
                // CustomDependencies
                "m_AdditionalRelatedFiles" => new AdditionalRelatedFiles(),
                "m_ChildResourceList" => new ChildResourceList(),
                // ExtraIntData
                // ExtraFloatData
                // ExtraStringData
                "m_SearchableUserData" => null,
                _ => throw new InvalidDataException($"Unknown struct in RED2 block: '{name}'"),
            };
        }
    }
}
