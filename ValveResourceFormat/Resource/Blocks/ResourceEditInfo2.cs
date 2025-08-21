using System.Globalization;
using System.IO;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RED2" block. CResourceEditInfo.
    /// </summary>
    public class ResourceEditInfo2 : ResourceEditInfo
    {
        public override BlockType Type => BlockType.RED2;

        private BinaryKV3 BackingData;

        public List<string> WeakReferenceList { get; } = [];
        public Dictionary<string, Dictionary<string, int>> SubassetReferences { get; private set; }
        public Dictionary<string, string[]> SubassetDefinitions { get; private set; }

        public ResourceEditInfo2()
        {
            //
        }

        public override void Read(BinaryReader reader)
        {
            var kv3 = new BinaryKV3
            {
                Offset = Offset,
                Size = Size,
                Resource = Resource,
            };

            kv3.Read(reader);
            BackingData = kv3;

            static void ReadItems<T>(BinaryKV3 kv3, List<T> list, string key, Func<KVObject, T> constructor)
            {
                var container = kv3.Data.Properties.GetValueOrDefault(key).Value as KVObject;
                ArgumentNullException.ThrowIfNull(container, key);
                ArgumentOutOfRangeException.ThrowIfEqual(container.IsArray, false, key);

                list.EnsureCapacity(container.Count);

                foreach (var item in container)
                {
                    var kvObject = item.Value as KVObject;
                    var newItem = constructor.Invoke(kvObject);
                    list.Add(newItem);
                }
            }

            ReadItems(kv3, InputDependencies, "m_InputDependencies", static (KVObject data) => new InputDependency(data));
            ReadItems(kv3, AdditionalInputDependencies, "m_AdditionalInputDependencies", static (KVObject data) => new InputDependency(data));
            ReadItems(kv3, ArgumentDependencies, "m_ArgumentDependencies", static (KVObject data) => new ArgumentDependency(data));
            ReadItems(kv3, SpecialDependencies, "m_SpecialDependencies", static (KVObject data) => new SpecialDependency(data));
            ReadItems(kv3, AdditionalRelatedFiles, "m_AdditionalRelatedFiles", static (KVObject data) => new AdditionalRelatedFile(data));

            var childResources = kv3.Data.GetArray<string>("m_ChildResourceList");
            ChildResourceList.AddRange(childResources);

            var weakReferences = kv3.Data.GetArray<string>("m_WeakReferenceList");
            if (weakReferences is not null)
            {
                WeakReferenceList.AddRange(weakReferences);
            }

            var searchableData = kv3.Data.GetProperty<KVObject>("m_SearchableUserData");
            SearchableUserData.Properties.EnsureCapacity(searchableData.Properties.Count);

            foreach (var property in searchableData.Properties)
            {
                SearchableUserData.Properties.Add(property.Key, property.Value);
            }

            var subassetReferences = kv3.Data.GetProperty<KVObject>("m_SubassetReferences");
            if (subassetReferences != null)
            {
                SubassetReferences = new(capacity: subassetReferences.Count);

                foreach (var property in subassetReferences)
                {
                    var subassetType = property.Key;
                    var perTypeReferencesKv = property.Value as KVObject;

                    var perTypeReferences = new Dictionary<string, int>(capacity: perTypeReferencesKv.Count);

                    foreach (var (refName, refCount) in perTypeReferencesKv)
                    {
                        perTypeReferences.Add(refName, Convert.ToInt32(refCount, CultureInfo.InvariantCulture));
                    }

                    SubassetReferences.Add(subassetType, perTypeReferences);
                }
            }

            var subassetDefinitions = kv3.Data.GetProperty<KVObject>("m_SubassetDefinitions");
            if (subassetDefinitions != null)
            {
                SubassetDefinitions = new(capacity: subassetDefinitions.Count);

                foreach (var property in subassetDefinitions)
                {
                    var subassetType = property.Key;
                    var definitions = subassetDefinitions.GetArray<string>(subassetType);

                    SubassetDefinitions.Add(subassetType, definitions);
                }
            }
        }

        public override void Serialize(Stream stream)
        {
            BackingData.Serialize(stream);
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            BackingData.WriteText(writer);
        }
    }
}
