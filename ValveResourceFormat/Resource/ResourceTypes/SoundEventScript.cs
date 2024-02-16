using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class SoundEventScript : NTRO
    {
        public Dictionary<string, KVObject> SoundEventScriptValue { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            var soundEvents = Output.GetArray<KVObject>("m_SoundEvents");
            SoundEventScriptValue = new(capacity: soundEvents.Length);

            foreach (var entry in soundEvents)
            {
                var soundName = entry.GetProperty<string>("m_SoundName");
                var soundValue = entry.GetProperty<string>("m_OperatorsKV");
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(soundValue));

                // Valve have duplicates, assume last is correct?
                SoundEventScriptValue[soundName] = KeyValues3.ParseKVFile(ms).Root;
            }
        }
    }
}
