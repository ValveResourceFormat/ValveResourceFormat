using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes
{
    public class SoundEventScript : NTRO
    {
        public Dictionary<string, string> SoundEventScriptValue { get; private set; } // TODO: be Dictionary<string, SomeKVObject>

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);
            SoundEventScriptValue = [];

            // Output is VSoundEventScript_t we need to iterate m_SoundEvents inside it.
            var soundEvents = (NTROArray)Output["m_SoundEvents"];

            foreach (var entry in soundEvents)
            {
                // sound is VSoundEvent_t
                var sound = ((NTROValue<NTROStruct>)entry).Value;
                var soundName = ((NTROValue<string>)sound["m_SoundName"]).Value;
                var soundValue = ((NTROValue<string>)sound["m_OperatorsKV"]).Value.Replace("\n", Environment.NewLine, StringComparison.InvariantCulture); // make sure we have new lines

                // Valve have duplicates, assume last is correct?
                SoundEventScriptValue.Remove(soundName);

                SoundEventScriptValue.Add(soundName, soundValue);
            }
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            foreach (var entry in SoundEventScriptValue)
            {
                writer.WriteLine("\"" + entry.Key + "\"");
                writer.WriteLine("{");
                writer.Indent++;
                // m_OperatorsKV wont be indented, so we manually indent it here, removing the last indent so we can close brackets later correctly.
                writer.Write(entry.Value.Replace(Environment.NewLine, Environment.NewLine + "\t", StringComparison.InvariantCulture).TrimEnd('\t'));
                writer.Indent--;
                writer.WriteLine("}");
                writer.WriteLine(string.Empty); // There is an empty line after every entry (including the last)
            }

            return writer.ToString();
        }
    }
}
