

#pragma warning disable CA1051 // Do not declare visible instance fields
namespace ValveResourceFormat.ShaderParser {

    public abstract class ShaderDataBlock {

        protected ShaderDataReader datareader;
        protected long start;

        protected ShaderDataBlock(ShaderDataReader datareader, long offset) {
            this.start = offset;
            this.datareader = datareader;
        }
        public int ReadIntegerAtPosition(int relOffset) {
            return datareader.ReadIntAtPosition(start + relOffset, rel: false);
        }
    }
}

