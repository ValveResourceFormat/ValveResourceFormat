using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes
{
    class SoundEventScript : NTRO
    {
        public Dictionary<string, string> soundEventScript; //TODO: be Dictionary<string, SomeKVObject>
        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);
            soundEventScript = new Dictionary<string, string>();

            //Output is VSoundEventScript_t we need to iterate m_SoundEvents inside it.
            var soundEvents = ((NTROSerialization.NTROArray)Output["m_SoundEvents"]);
            foreach (NTROSerialization.NTROValue entry in soundEvents)
            {
                //sound is VSoundEvent_t
                var sound = ((NTROSerialization.NTROValue<NTROSerialization.NTROStruct>)entry).value;
                var soundName = ((NTROSerialization.NTROValue<string>) sound["m_SoundName"]).value;
                var soundValue = ((NTROSerialization.NTROValue<string>) sound["m_OperatorsKV"]).value.Replace("\n", Environment.NewLine); //make sure we have new lines;
                soundEventScript.Add(soundName, soundValue);
            }
        }
        public override string ToString()
        {
            using (var output = new StringWriter())
            using (var Writer = new IndentedTextWriter(output, "\t"))
            {
                foreach (KeyValuePair<string, string> entry in soundEventScript)
                {
                    Writer.WriteLine(entry.Key);
                    Writer.WriteLine("{");
                    Writer.Indent++;
                    //m_OperatorsKV wont be indented, so we manually indent it here, removing the last indent so we can close brackets later correctly.
                    Writer.Write(entry.Value.Replace(Environment.NewLine, Environment.NewLine + "\t").TrimEnd('\t'));
                    Writer.Indent--;
                    Writer.WriteLine("}");
                    Writer.WriteLine(""); //There is an empty line after every entry (including the last)
                }
                return output.ToString();
            }
        }
    }
}
