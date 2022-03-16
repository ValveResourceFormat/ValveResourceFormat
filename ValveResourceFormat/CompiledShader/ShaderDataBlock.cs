
namespace ValveResourceFormat.CompiledShader
{
    public abstract class ShaderDataBlock
    {
        public ShaderDataReader datareader { get; }
        protected long start { get; }
        protected ShaderDataBlock(ShaderDataReader datareader)
        {
            this.start = datareader.BaseStream.Position;
            this.datareader = datareader;
        }
    }
}

