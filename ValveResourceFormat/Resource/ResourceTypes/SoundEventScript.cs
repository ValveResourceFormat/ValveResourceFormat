using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
namespace ValveResourceFormat.ResourceTypes
{
    public class SoundEventScript : NTRO
    {
        public Dictionary<string, string> SoundEventScriptValue; // TODO: be Dictionary<string, SomeKVObject>

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);
            SoundEventScriptValue = new Dictionary<string, string>();

            // Output is VSoundEventScript_t we need to iterate m_SoundEvents inside it.
            var soundEvents = (NTROSerialization.NTROArray)Output["m_SoundEvents"];

            foreach (NTROSerialization.NTROValue entry in soundEvents)
            {
                // sound is VSoundEvent_t
                var sound = ((NTROSerialization.NTROValue<NTROSerialization.NTROStruct>)entry).Value;
                var soundName = ((NTROSerialization.NTROValue<string>)sound["m_SoundName"]).Value;
                var soundValue = ((NTROSerialization.NTROValue<string>)sound["m_OperatorsKV"]).Value.Replace("\n", Environment.NewLine); // make sure we have new lines

                if (SoundEventScriptValue.ContainsKey(soundName))
                {
                    // Valve have duplicates, assume last is correct?
                    SoundEventScriptValue.Remove(soundName);
                }

                SoundEventScriptValue.Add(soundName, soundValue);
            }
        }

        public override string ToString()
        {
            using (var output = new StringWriter())
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                foreach (KeyValuePair<string, string> entry in SoundEventScriptValue)
                {
                    writer.WriteLine("\"" + entry.Key + "\"");
                    writer.WriteLine("{");
                    writer.Indent++;
                    // m_OperatorsKV wont be indented, so we manually indent it here, removing the last indent so we can close brackets later correctly.
                    writer.Write(entry.Value.Replace(Environment.NewLine, Environment.NewLine + "\t").TrimEnd('\t'));
                    writer.Indent--;
                    writer.WriteLine("}");
                    writer.WriteLine(string.Empty); // There is an empty line after every entry (including the last)
                }

                return output.ToString();
            }
        }
    }
}
