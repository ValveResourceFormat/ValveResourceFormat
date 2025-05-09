
namespace ValveResourceFormat.CompiledShader;

public abstract class ShaderDataBlock
{
    public ShaderDataReader DataReader { get; }
    protected long Start { get; }

    protected ShaderDataBlock(ShaderDataReader datareader)
    {
        Start = datareader.BaseStream.Position;
        DataReader = datareader;
    }
}
