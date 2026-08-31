using System.Globalization;
using System.IO;
using System.Text;
using ValveKeyValue;
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
    public string Name { get; }
    /// <summary>Gets the 32-bit MurmurHash2 of the name.</summary>
    public uint Murmur32 { get; }
    /// <summary>Gets the variable type.</summary>
    public VfxVariableType VfxType { get; }
    /// <summary>Gets the index of the variable this attribute is bound to, or -1 when it is not bound to one.</summary>
    public short VariableBinding { get; }
    /// <summary>Gets the dynamic expression bytecode.</summary>
    public byte[]? DynExpression { get; }
    /// <summary>Gets the constant value.</summary>
    public object? ConstValue { get; }

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VfxShaderAttribute(KVObject data)
    {
        Name = data.GetStringProperty("m_Name");
        VfxType = (VfxVariableType)data.GetInt32Property("m_type");
        VariableBinding = (short)data.GetInt32Property("m_nVariableBinding");

        if (data.TryGetValue("m_value", out var value) && value.ValueType != KVValueType.Null)
        {
            ConstValue = VfxType switch
            {
                VfxVariableType.Int => (int)value,
                VfxVariableType.Bool => (bool)value,
                VfxVariableType.String => (string)value,
                VfxVariableType.Float when value.IsArray => (float)value[0],
                VfxVariableType.Float => (float)value,
                VfxVariableType.Float2 when value.IsArray => value.ToVector2(),
                VfxVariableType.Float3 when value.IsArray => value.ToVector3(),
                VfxVariableType.Float4 when value.IsArray => value.ToVector4(),
                _ => (object)value,
            };
        }

        if (data.GetArray<byte>("m_expr") is byte[] expression)
        {
            DynExpression = expression;
        }

        Murmur32 = StringToken.Store(Name);
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderAttribute(BinaryReader datareader)
    {
        Name = datareader.ReadNullTermString(Encoding.UTF8);
        Murmur32 = datareader.ReadUInt32();
        var murmurCheck = StringToken.Store(Name);
        if (Murmur32 != murmurCheck)
        {
            throw new ShaderParserException("Murmur check failed on header name");
        }
        VfxType = (VfxVariableType)datareader.ReadByte();
        VariableBinding = datareader.ReadInt16();

        if (VariableBinding != -1)
        {
            return;
        }

        var dynExpLen = datareader.ReadInt32();
        if (dynExpLen > 0)
        {
            DynExpression = datareader.ReadBytes(dynExpLen);
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
    /// Returns a formatted string with the attribute name, hash, type, variable binding, and either the dynamic expression or constant value.
    /// </remarks>
    public override string ToString()
    {
        if (DynExpression != null)
        {
            return $"{Name,-40} 0x{Murmur32:x08}  {VfxType,-15} {VariableBinding,-3}  {ParseDynamicExpression(DynExpression)}";
        }
        else
        {
            return $"{Name,-40} 0x{Murmur32:x08}  {VfxType,-15} {VariableBinding,-3}  {ConstValue}";
        }
    }
}
