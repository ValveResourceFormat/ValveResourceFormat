using System.Diagnostics;
using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Header block for shader features files.
/// </summary>
public class FeaturesHeaderBlock : ShaderDataBlock
{
    /// <summary>Gets the features file version.</summary>
    public int Version { get; }
    /// <summary>Gets the file description.</summary>
    public string FileDescription { get; }
    /// <summary>Gets whether this is a development shader.</summary>
    public bool DevShader { get; }
    /// <summary>Gets the array of available program types.</summary>
    public bool[] AvailablePrograms { get; }
    /// <summary>Gets the list of shader modes.</summary>
    public List<(string Name, string Shader, string StaticConfig, int Value)> Modes { get; } = [];

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public FeaturesHeaderBlock(KVObject data)
    {
        Version = data.GetInt32Property("m_nVersion");
        FileDescription = data.GetStringProperty("m_description");
        DevShader = data.GetProperty<bool>("m_bDevShader");
        AvailablePrograms = data.GetArray<bool>("m_bHasShaderProgram");

        var modeArray = data.GetArray("m_modeArray");
        Modes.EnsureCapacity(modeArray.Length);

        foreach (var modeObj in modeArray)
        {
            var name = modeObj.GetStringProperty("m_szName");
            var shader = modeObj.GetStringProperty("m_szShaderFallback");

            var mode = (name, shader, ComboName: string.Empty, ComboValue: -1);

            var settings = modeObj.GetArray<KVObject>("m_staticComboSettings");
            if (settings.Length > 0)
            {
                Debug.Assert(settings.Length <= 1, "CVfxModeSettings with more than one combo.");

                var setting = settings[0];
                mode.ComboName = setting.GetProperty<string>("m_szStaticCombo");
                mode.ComboValue = setting.GetInt32Property("m_nValue");
            }

            Modes.Add(mode);
        }
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
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
