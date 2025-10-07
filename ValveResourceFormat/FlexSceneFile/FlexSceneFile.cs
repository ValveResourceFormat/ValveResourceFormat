using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.FlexSceneFile
{
    /// <summary>
    /// Represents a Valve flex scene file containing facial animation data.
    /// </summary>
    public partial class FlexSceneFile
    {
        /// <summary>
        /// Represents a flex weight with influence values.
        /// </summary>
        public struct FlexWeight
        {
            /// <summary>Gets or sets the weight value.</summary>
            public float Weight { get; set; }

            /// <summary>Gets or sets the influence value.</summary>
            public float Influence { get; set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="FlexWeight"/> struct.
            /// </summary>
            public FlexWeight(float weight, float influence)
            {
                Weight = weight;
                Influence = influence;
            }
        }

        /// <summary>
        /// Represents a flex setting for a phoneme.
        /// </summary>
        public class FlexSetting
        {
            /// <summary>Gets the name of the flex setting.</summary>
            public string Name { get; init; }

            /// <summary>Gets the phoneme code.</summary>
            public int Phoneme { get; init; }
            /// <summary>
            /// Maps flex controllers to expression settings. Use <see cref="FlexSceneFile.KeyNames"/> to find the flex controller's key.
            /// </summary>
            public Dictionary<int, FlexWeight> Settings { get; } = [];

            /// <summary>
            /// Initializes a new instance of the <see cref="FlexSetting"/> class.
            /// </summary>
            public FlexSetting(string name)
            {
                Name = name;
                Phoneme = TextToPhoneme(name);
            }

            /// <summary>
            /// Adds a flex weight for the specified key.
            /// </summary>
            public void AddWeight(int key, FlexWeight data)
            {
                Settings.Add(key, data);
            }

            /// <summary>
            /// Returns expression settings for a flex controller. Use <see cref="FlexSceneFile.KeyNames"/> to find the flex controller's key.
            /// </summary>
            /// <remarks>Returns the default value when no weight is stored for the supplied key.</remarks>
            public FlexWeight GetWeight(int key)
            {
                if (Settings.TryGetValue(key, out var weight))
                {
                    return weight;
                }

                return default;
            }
        }

        /// <summary>
        /// The magic number for flex scene files.
        /// </summary>
        public const uint MAGIC = 0x00564645; //EFV\0

        /// <summary>Gets the version of the flex scene file.</summary>
        public int Version { get; private set; }

        /// <summary>Gets the name of the flex scene.</summary>
        public string Name { get; private set; }

        /// <summary>Gets the names of all flex controller keys.</summary>
        public string[] KeyNames { get; private set; }

        /// <summary>Gets all flex settings.</summary>
        public FlexSetting[] FlexSettings { get; private set; }
        private readonly Dictionary<int, int> phonemeToFlexSetting = [];

        /// <summary>
        /// Returns the flex settings for the specified phoneme code, or null if no data is stored for the specified phoneme.
        /// </summary>
        public FlexSetting GetSettingForPhonemeCode(int phoneme)
        {
            if (phonemeToFlexSetting.TryGetValue(phoneme, out var i))
            {
                return FlexSettings[i];
            }

            return null;
        }

        /// <summary>
        /// Reads the flex scene data from the specified stream.
        /// </summary>
        public void Read(Stream input)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, true);

            var startPosition = reader.BaseStream.Position;

            var magic = reader.ReadInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException("Stream is not VFE", magic, nameof(magic));
            }

            Version = reader.ReadInt32();
            if (Version != 0)
            {
                throw new UnexpectedMagicException("Unexpected VFE version", Version, nameof(Version));
            }

            var nameEndPosition = reader.BaseStream.Position + 64;
            Name = reader.ReadNullTermString(Encoding.ASCII);
            reader.BaseStream.Position = nameEndPosition;

            reader.BaseStream.Position += 4; //Length of file
            var numFlexSettings = reader.ReadInt32(); //Number of phonemes (rows in phonemes.txt)
            var flexSettingIndex = reader.ReadInt32(); //Position - Start of flex settings for each phoneme
            reader.BaseStream.Position += 4; // [nameindex] Position - Filename string
            reader.BaseStream.Position += 4; // [numindexes] Number of indexes in the indexindex map
            reader.BaseStream.Position += 4; // [indexindex] Position - numindexes number of int32's, seems to be used only by flexsettings to point to it's own id
            var numKeys = reader.ReadInt32(); //Number of used flex controllers ($keys in phonemes.txt)
            var keyNameIndex = reader.ReadInt32(); //Position - Points to numKeys number of positions, which point to each flex controller's string
            reader.BaseStream.Position += 4; // [keymappingindex] Position - Points to numKeys * 4 number of 0xFF bytes in all cases found

            FlexSettings = ReadFlexSettings(reader, flexSettingIndex, numFlexSettings);
            KeyNames = ReadKeyNames(reader, keyNameIndex, numKeys);
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Read(fs);
        }

        private FlexSetting[] ReadFlexSettings(BinaryReader reader, int flexSettingIndex, int numFlexSettings)
        {
            reader.BaseStream.Position = flexSettingIndex;

            var flexSettings = new FlexSetting[numFlexSettings];
            for (var i = 0; i < numFlexSettings; i++)
            {
                var startPosition = (int)reader.BaseStream.Position;

                var nameIndex = startPosition + reader.ReadInt32();
                reader.BaseStream.Position += 4; //Obsolete - all zeros
                var numSettings = reader.ReadInt32();
                reader.BaseStream.Position += 4; //int32 used to index into the indexindex array - always points to a number equal to i
                reader.BaseStream.Position += 4; //Obsolete - all zeros
                var settingIndex = startPosition + reader.ReadInt32();

                flexSettings[i] = ReadFlexWeights(reader, nameIndex, settingIndex, numSettings);
                phonemeToFlexSetting[flexSettings[i].Phoneme] = i;
            }

            return flexSettings;
        }

        private static FlexSetting ReadFlexWeights(BinaryReader reader, int nameIndex, int settingIndex, int numSettings)
        {
            var startPosition = reader.BaseStream.Position;

            reader.BaseStream.Position = nameIndex;
            var name = reader.ReadNullTermString(Encoding.UTF8);

            var flexSetting = new FlexSetting(name);
            reader.BaseStream.Position = settingIndex;

            for (var i = 0; i < numSettings; i++)
            {
                var key = reader.ReadInt32();
                var weight = reader.ReadSingle();
                var influence = reader.ReadSingle();
                flexSetting.AddWeight(key, new FlexWeight(weight, influence));
            }

            reader.BaseStream.Position = startPosition;

            return flexSetting;
        }

        private static string[] ReadKeyNames(BinaryReader reader, int keyNameIndex, int numKeys)
        {
            reader.BaseStream.Position = keyNameIndex;

            var keyNames = new string[numKeys];
            for (var i = 0; i < numKeys; i++)
            {
                var keyNamePosition = reader.ReadInt32();
                var lastPosition = reader.BaseStream.Position;

                reader.BaseStream.Position = keyNamePosition;
                keyNames[i] = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = lastPosition;
            }

            return keyNames;
        }

        private static int TextToPhoneme(string text)
        {
            return text switch
            {
                "er" => 0x025a,
                "h" => 'h',
                "k" => 'k',
                "m" => 'm',
                "p" => 'p',
                "w" => 'w',
                "f" => 'f',
                "v" => 'v',
                "r" => 0x0279,
                "r2" => 'r',
                "r3" => 0x027b,
                "er2" => 0x025d,
                "dh" => 0x00f0,
                "th" => 0x03b8,
                "sh" => 0x0283,
                "jh" => 0x02a4,
                "ch" => 0x02a7,
                "s" => 's',
                "z" => 'z',
                "d" => 'd',
                "d2" => 0x027e,
                "l" => 'l',
                "l2" => 0x026b,
                "n" => 'n',
                "t" => 't',
                "ow" => 'o',
                "uw" => 'u',
                "ey" => 'e',
                "ae" => 0x00e6,
                "aa" => 0x0251,
                "aa2" => 'a',
                "iy" => 'i',
                "y" => 'j',
                "ah" => 0x028c,
                "ao" => 0x0254,
                "ax" => 0x0259,
                "ax2" => 0x025c,
                "eh" => 0x025b,
                "ih" => 0x026a,
                "ih2" => 0x0268,
                "uh" => 0x028a,
                "g" => 'g',
                "g2" => 0x0261,
                "hh" => 'h',
                "hh2" => 0x0266,
                "c" => 'k',
                "nx" => 0x014b,
                "zh" => 0x0292,
                "b" => 'b',
                _ => '_',
            };
        }
    }
}
