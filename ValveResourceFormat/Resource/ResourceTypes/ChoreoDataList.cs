using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Parser;
using LzmaDecoder = SevenZip.Compression.LZMA.Decoder;

namespace ValveResourceFormat.ResourceTypes
{
    public class ChoreoDataList : ResourceData
    {
        public int Version { get; private set; }
        public int Unk2 { get; private set; }
        public ChoreoScene[] Scenes { get; private set; }
        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            //header
            Version = reader.ReadInt32();
            var sceneCount = reader.ReadInt32();
            var strings = ReadStrings(reader);
            Unk2 = reader.ReadInt32();

            //scene entries
            Scenes = ReadScenes(reader, sceneCount, strings);
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            foreach (var item in Scenes)
            {
                writer.WriteLine(item.Name);
            }
        }

        private static long ReadPosition(BinaryReader reader)
        {
            return reader.BaseStream.Position + reader.ReadInt32();
        }


        private static ChoreoScene[] ReadScenes(BinaryReader reader, int sceneCount, string[] strings)
        {
            {
                //todo: remove

                Console.WriteLine($"Strings:");
                for (var i = 0; i < strings.Length; i++)
                {
                    Console.WriteLine($"{i}: \"{strings[i]}\"");
                }
            }
            var scenes = new ChoreoScene[sceneCount];
            for (var i = 0; i < sceneCount; i++)
            {
                scenes[i] = ReadScene(reader, strings);
            }
            return scenes;
        }

        private static ChoreoScene ReadScene(BinaryReader reader, string[] strings)
        {
            var namePosition = ReadPosition(reader);
            var blockPosition = ReadPosition(reader);
            var length = reader.ReadInt32();
            var sceneDuration = reader.ReadInt32();
            var sceneSoundDuration = reader.ReadInt32();
            var unk1 = reader.ReadInt32(); //TODO: This is 1 if vcd has sounds, 0 otherwise. Can anything else be here? Why does this take up 4 bytes

            var previousPosition = reader.BaseStream.Position;

            reader.BaseStream.Position = namePosition;
            var name = reader.ReadNullTermString(Encoding.ASCII);

            reader.BaseStream.Position = blockPosition;
            var sceneBlock = ReadSceneBlock(reader, length);

            reader.BaseStream.Position = previousPosition;

            {
                //todo: remove
                var debugName = name.Replace(".vcd", ".bvcd");
                var path = "vcdtest/" + Path.GetDirectoryName(debugName);
                Directory.CreateDirectory(path);
                using var debugFile = File.OpenWrite($"vcdtest/{debugName}");
                debugFile.Write(sceneBlock);
            }

            using var sceneStream = new MemoryStream(sceneBlock);
            var scene = BVCDParser.Parse(sceneStream, strings);
            scene.Name = name;
            scene.Duration = sceneDuration;
            scene.SoundDuration = sceneSoundDuration;
            scene.Unk1 = unk1;

            return scene;
        }

        private static byte[] ReadSceneBlock(BinaryReader reader, int length)
        {
            var id = new string(reader.ReadChars(4));
            if (id == "LZMA")
            {
                var uncompressedLength = reader.ReadInt32();
                var compressedLength = reader.ReadInt32();

                var lzmaDecoder = new LzmaDecoder();
                lzmaDecoder.SetDecoderProperties(reader.ReadBytes(5));
                var compressedBuffer = reader.ReadBytes(compressedLength);
                using (var inputStream = new MemoryStream(compressedBuffer))
                using (var outStream = new MemoryStream(uncompressedLength))
                {
                    lzmaDecoder.Code(inputStream, outStream, compressedBuffer.Length, uncompressedLength, null);
                    return outStream.ToArray();
                }
            }
            else
            {
                reader.BaseStream.Position -= 4;
                return reader.ReadBytes(length);
            }
        }

        private static string[] ReadStrings(BinaryReader reader)
        {
            var stringOffsetsPosition = ReadPosition(reader);
            var stringCount = reader.ReadInt32();
            var stringOffsets = ReadStringOffsets(reader, stringOffsetsPosition, stringCount);

            var stringsPosition = ReadPosition(reader);
            return ReadStringsData(reader, stringsPosition, stringOffsets);
        }

        private static int[] ReadStringOffsets(BinaryReader reader, long lengthsPosition, int stringCount)
        {
            var previousPosition = reader.BaseStream.Position;
            reader.BaseStream.Position = lengthsPosition;

            var lengths = new int[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                lengths[i] = reader.ReadInt32();
            }

            reader.BaseStream.Position = previousPosition;
            return lengths;
        }
        private static string[] ReadStringsData(BinaryReader reader, long stringsPosition, int[] stringOffsets)
        {
            var previousPosition = reader.BaseStream.Position;

            var strings = new string[stringOffsets.Length];
            for (var i = 0; i < stringOffsets.Length; i++)
            {
                reader.BaseStream.Position = stringsPosition + stringOffsets[i];
                strings[i] = reader.ReadNullTermString(Encoding.ASCII);
            }

            reader.BaseStream.Position = previousPosition;
            return strings;
        }
    }
}
