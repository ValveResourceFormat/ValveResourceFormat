using System.Globalization;
using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.ThirdParty;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Shader attribute with name, type, and value or expression.
/// </summary>
public class VfxShaderAttribute
{
    /// <summary>Gets the attribute name.</summary>
    public string Name0 { get; }
    /// <summary>Gets the Murmur2 hash of the name.</summary>
    public uint Murmur32 { get; }
    /// <summary>Gets the variable type.</summary>
    public VfxVariableType VfxType { get; }
    /// <summary>Gets the linked parameter index.</summary>
    public short LinkedParameterIndex { get; }
    /// <summary>Gets the dynamic expression length.</summary>
    public int DynExpLen { get; } = -1;
    /// <summary>Gets the dynamic expression bytecode.</summary>
    public byte[]? DynExpression { get; }
    /// <summary>Gets the constant value.</summary>
    public object? ConstValue { get; }

    /// <summary>
    /// Initializes a new instance from KeyValues data.
    /// </summary>
    public VfxShaderAttribute(KVObject data)
    {
        Name0 = data.GetProperty<string>("m_Name");
        VfxType = (VfxVariableType)data.GetInt32Property("m_type");
        LinkedParameterIndex = (short)data.GetInt32Property("m_nVariableBinding");

        var value = data.GetProperty<object?>("m_value");
        ConstValue = VfxType switch
        {
            VfxVariableType.Int => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            VfxVariableType.Float when value is KVObject { IsArray: true } kv => kv.ToVector2()[0],
            VfxVariableType.Float2 when value is KVObject { IsArray: true } kv => kv.ToVector2(),
            VfxVariableType.Float3 when value is KVObject { IsArray: true } kv => kv.ToVector3(),
            VfxVariableType.Float4 when value is KVObject { IsArray: true } kv => kv.ToVector4(),
            _ => value,
        };

        if (data.GetArray<byte>("m_expr") is byte[] expression)
        {
            DynExpLen = expression.Length;
            DynExpression = expression;
        }

        Murmur32 = StringToken.Store(Name0);
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderAttribute(BinaryReader datareader)
    {
        Name0 = datareader.ReadNullTermString(Encoding.UTF8);
        Murmur32 = datareader.ReadUInt32();
        var murmurCheck = MurmurHash2.Hash(Name0.ToLowerInvariant(), StringToken.MURMUR2SEED);
        if (Murmur32 != murmurCheck)
        {
            throw new ShaderParserException("Murmur check failed on header name");
        }
        VfxType = (VfxVariableType)datareader.ReadByte();
        LinkedParameterIndex = datareader.ReadInt16();

        if (LinkedParameterIndex != -1)
        {
            return;
        }

        DynExpLen = datareader.ReadInt32();
        if (DynExpLen > 0)
        {
            DynExpression = datareader.ReadBytes(DynExpLen);
            return;
        }

        ConstValue = VfxType switch
        {
            VfxVariableType.Float => datareader.ReadSingle(),
            VfxVariableType.Int => datareader.ReadInt32(),
            VfxVariableType.Bool => datareader.ReadByte() != 0,
            VfxVariableType.String => datareader.ReadNullTermString(Encoding.UTF8),
            VfxVariableType.Float2 => new Vector2(datareader.ReadSingle(), datareader.ReadSingle()),
            VfxVariableType.Float3 => new Vector3(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
            VfxVariableType.Float4 => new Vector4(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
            _ => throw new ShaderParserException($"Unexpected attribute type {VfxType} has a constant value."),
        };

    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a formatted string with the attribute name, hash, type, parameter index, and either the dynamic expression or constant value.
    /// </remarks>
    public override string ToString()
    {
        if (DynExpression != null)
        {
            return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {ParseDynamicExpression(DynExpression)}";
        }
        else
        {
            return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {ConstValue}";
        }
    }
}
