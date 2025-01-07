using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public struct ResponseRuleInclude
    {
        public string Name { get; set; }
        public int Flags1 { get; set; }
        public int Flags2 { get; set; }
    }

    public class ResponseRules : ResourceData
    {
        public byte Arg1 { get; private set; }
        public byte Arg2 { get; private set; }
        public byte Arg3 { get; private set; }
        public byte Arg4 { get; private set; }
        public ResponseRuleInclude[] Includes { get; private set; }
        public string File { get; private set; }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Arg1 = reader.ReadByte();
            Arg2 = reader.ReadByte();
            Arg3 = reader.ReadByte();
            Arg4 = reader.ReadByte();

            var includeCount = reader.ReadUInt16();
            Includes = new ResponseRuleInclude[includeCount];

            for (var i = 0; i < includeCount; i++)
            {
                var dependency = reader.ReadNullTermString(Encoding.UTF8);
                var flags1 = reader.ReadInt32();
                var flags2 = reader.ReadInt32();

                Includes[i] = new ResponseRuleInclude
                {
                    Name = dependency,
                    Flags1 = flags1,
                    Flags2 = flags2,
                };
            }

            var relativeOffsetHere = reader.BaseStream.Position - Offset;
            File = Encoding.UTF8.GetString(reader.ReadBytes((int)(Size - relativeOffsetHere)));
        }

        public override string ToString()
        {
            return File;
        }
    }
}
