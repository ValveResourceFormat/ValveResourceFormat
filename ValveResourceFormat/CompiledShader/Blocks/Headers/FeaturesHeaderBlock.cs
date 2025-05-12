using System.Diagnostics;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

public class FeaturesHeaderBlock : ShaderDataBlock
{
    public int Version { get; }
    public string FileDescription { get; }
    public bool DevShader { get; }
    public bool[] AvailablePrograms { get; }
    public List<(string Name, string Shader, string StaticConfig, int Value)> Modes { get; } = [];

    public FeaturesHeaderBlock(int version, ShaderDataReader datareader, int totalShaderVariants) : base(datareader)
    {
        Version = datareader.ReadInt32();

        var nameLength = datareader.ReadInt32();
        FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
        UnexpectedMagicException.Assert(FileDescription.Length == nameLength, nameLength);

        // For some reason valve is storing booleans as ints
        DevShader = datareader.ReadInt32() != 0;

        AvailablePrograms = new bool[totalShaderVariants];

        for (var i = 0; i < totalShaderVariants; i++)
        {
            AvailablePrograms[i] = datareader.ReadInt32() != 0;
        }

        var modeCount = datareader.ReadInt32();

        for (var i = 0; i < modeCount; i++)
        {
            // CVfxMode::Unserialize
            var name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            var shader = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;

            var modeSettingsCount = datareader.ReadInt32();

            var static_config = string.Empty;
            var value = -1;
            if (modeSettingsCount > 0)
            {
                Debug.Assert(modeSettingsCount == 1); // we never supported more than 1 here

                for (var j = 0; j < modeSettingsCount; j++)
                {
                    // CVfxModeSettings::Unserialize
                    static_config = datareader.ReadNullTermStringAtPosition();
                    datareader.BaseStream.Position += 64;
                    value = datareader.ReadInt32();
                }
            }
            Modes.Add((name, shader, static_config, value));
        }
    }
}
