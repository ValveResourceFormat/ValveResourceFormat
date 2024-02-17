using System.IO;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RED2" block. CResourceEditInfo.
    /// </summary>
    public class ResourceEditInfo2 : ResourceEditInfo
    {
        public override BlockType Type => BlockType.RED2;

        private BinaryKV3 BackingData;

        //public ? WeakReferenceList { get; private set; }
        public KVObject SearchableUserData { get; private set; }

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

            ConstructSpecialDependencies();
            ConstuctInputDependencies();
            ConstuctAdditionalInputDependencies();

            SearchableUserData = kv3.AsKeyValueCollection().GetSubCollection("m_SearchableUserData");
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

        private void ConstructSpecialDependencies()
        {
            var specialDependenciesRedi = new SpecialDependencies();
            var specialDependencies = BackingData.AsKeyValueCollection().GetArray("m_SpecialDependencies");

            foreach (var specialDependency in specialDependencies)
            {
                var specialDependencyRedi = new SpecialDependencies.SpecialDependency
                {
                    String = specialDependency.GetProperty<string>("m_String"),
                    CompilerIdentifier = specialDependency.GetProperty<string>("m_CompilerIdentifier"),
                    Fingerprint = specialDependency.GetIntegerProperty("m_nFingerprint"),
                    UserData = specialDependency.GetIntegerProperty("m_nUserData"),
                };

                specialDependenciesRedi.List.Add(specialDependencyRedi);
            }

            Structs.Add(REDIStruct.SpecialDependencies, specialDependenciesRedi);
        }

        private void ConstuctInputDependencies()
        {
            var dependenciesRedi = new InputDependencies();
            var dependencies = BackingData.AsKeyValueCollection().GetArray("m_InputDependencies");

            foreach (var dependency in dependencies)
            {
                var dependencyRedi = new InputDependencies.InputDependency
                {
                    ContentRelativeFilename = dependency.GetProperty<string>("m_RelativeFilename"),
                    ContentSearchPath = dependency.GetProperty<string>("m_SearchPath"),
                    FileCRC = (uint)dependency.GetUnsignedIntegerProperty("m_nFileCRC"),
                };

                dependenciesRedi.List.Add(dependencyRedi);
            }

            Structs.Add(REDIStruct.InputDependencies, dependenciesRedi);
        }

        private void ConstuctAdditionalInputDependencies()
        {
            var dependenciesRedi = new InputDependencies();
            var dependencies = BackingData.AsKeyValueCollection().GetArray("m_AdditionalInputDependencies");

            foreach (var dependency in dependencies)
            {
                var dependencyRedi = new InputDependencies.InputDependency
                {
                    ContentRelativeFilename = dependency.GetProperty<string>("m_RelativeFilename"),
                    ContentSearchPath = dependency.GetProperty<string>("m_SearchPath"),
                    FileCRC = (uint)dependency.GetUnsignedIntegerProperty("m_nFileCRC"),
                };

                dependenciesRedi.List.Add(dependencyRedi);
            }

            Structs.Add(REDIStruct.AdditionalInputDependencies, dependenciesRedi);
        }

        /*
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
        */
    }
}
