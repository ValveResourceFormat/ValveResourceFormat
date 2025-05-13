using System.Diagnostics;
using System.IO;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

public class FeaturesHeaderBlock : ShaderDataBlock
{
    public int Version { get; }
    public string FileDescription { get; }
    public bool DevShader { get; }
    public bool[] AvailablePrograms { get; }
    public List<(string Name, string Shader, string StaticConfig, int Value)> Modes { get; } = [];

    public FeaturesHeaderBlock(BinaryReader datareader, int programTypesCount) : base(datareader)
    {
        Version = datareader.ReadInt32(); // this is probably not a version

        var nameLength = datareader.ReadInt32();
        FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
        UnexpectedMagicException.Assert(FileDescription.Length == nameLength, nameLength);

        // For some reason valve is storing booleans as ints
        DevShader = datareader.ReadInt32() != 0;

        AvailablePrograms = new bool[programTypesCount];

        for (var i = 0; i < programTypesCount; i++)
        {
            AvailablePrograms[i] = datareader.ReadInt32() != 0;
        }

        var modeCount = datareader.ReadInt32();

        for (var i = 0; i < modeCount; i++)
        {
            // CVfxMode::Unserialize
            var name = ReadStringWithMaxLength(datareader, 64);
            var shader = ReadStringWithMaxLength(datareader, 64);

            var modeSettingsCount = datareader.ReadInt32();

            var static_config = string.Empty;
            var value = -1;
            if (modeSettingsCount > 0)
            {
                Debug.Assert(modeSettingsCount == 1); // we never supported more than 1 here

                for (var j = 0; j < modeSettingsCount; j++)
                {
                    // CVfxModeSettings::Unserialize
                    static_config = ReadStringWithMaxLength(datareader, 64);
                    value = datareader.ReadInt32();
                }
            }
            Modes.Add((name, shader, static_config, value));
        }
    }
}
