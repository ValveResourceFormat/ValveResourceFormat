using System.Text;
using ValveResourceFormat.ThirdParty;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader;

public class VfxShaderAttribute
{
    public string Name0 { get; }
    public uint Murmur32 { get; }
    public Vfx.Type VfxType { get; }
    public short LinkedParameterIndex { get; }
    public byte[] HeaderCode { get; }
    public int DynExpLen { get; } = -1;
    public byte[] DynExpression { get; }
    public string DynExpEvaluated { get; }
    public object ConstValue { get; }

    public VfxShaderAttribute(ShaderDataReader datareader)
    {
        Name0 = datareader.ReadNullTermString(Encoding.UTF8);
        Murmur32 = datareader.ReadUInt32();
        var murmurCheck = MurmurHash2.Hash(Name0.ToLowerInvariant(), StringToken.MURMUR2SEED);
        if (Murmur32 != murmurCheck)
        {
            throw new ShaderParserException("Murmur check failed on header name");
        }
        VfxType = (Vfx.Type)datareader.ReadByte();
        LinkedParameterIndex = datareader.ReadInt16();

        if (LinkedParameterIndex != -1)
        {
            return;
        }

        DynExpLen = datareader.ReadInt32();
        if (DynExpLen > 0)
        {
            DynExpression = datareader.ReadBytes(DynExpLen);
            DynExpEvaluated = ParseDynamicExpression(DynExpression);
            return;
        }

        ConstValue = VfxType switch
        {
            Vfx.Type.Float => datareader.ReadSingle(),
            Vfx.Type.Int => datareader.ReadInt32(),
            Vfx.Type.Bool => datareader.ReadByte() != 0,
            Vfx.Type.String => datareader.ReadNullTermString(Encoding.UTF8),
            Vfx.Type.Float2 => new Vector2(datareader.ReadSingle(), datareader.ReadSingle()),
            Vfx.Type.Float3 => new Vector3(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
            Vfx.Type.Float4 => new Vector4(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
            _ => throw new ShaderParserException($"Unexpected attribute type {VfxType} has a constant value."),
        };

    }

    public override string ToString()
    {
        if (DynExpLen > 0)
        {
            return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {DynExpEvaluated}";
        }
        else
        {
            return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {ConstValue}";
        }
    }
}
