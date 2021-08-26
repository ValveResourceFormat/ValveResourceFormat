
namespace ValveResourceFormat.CompiledShader
{
    public abstract class ShaderDataBlock
    {
        public ShaderDataReader datareader { get; }
        protected int start { get; }
        protected ShaderDataBlock(ShaderDataReader datareader, int offsetAtStartOfBlock)
        {
            this.start = offsetAtStartOfBlock;
            this.datareader = datareader;
        }
    }
}

