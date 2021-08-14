using System;
using System.Collections.Generic;
using System.Diagnostics;
using static ValveResourceFormat.ShaderParser.ShaderUtilHelpers;

#pragma warning disable CA1051 // Do not declare visible instance fields
namespace ValveResourceFormat.ShaderParser {

    public class ZDataBlock : ShaderDataBlock {
        public int blockId;
        public int h0;
        public int h1;
        public int h2;
        public byte[] dataload;
        public ZDataBlock(ShaderDataReader datareader, long start, int blockId) : base(datareader, start) {
            this.blockId = blockId;
            h0 = datareader.ReadInt();
            h1 = datareader.ReadInt();
            h2 = datareader.ReadInt();
            if (h0 > 0) {
                dataload = datareader.ReadBytes(h0 * 4);
            }
        }
    }

    public class GlslSource : ShaderDataBlock {

        public int sourceId;
        public int offset0;
        public int arg0 = -1;
        public int offset1 = -1;
        public byte[] sourcebytes;
        public byte[] fileId;

        public GlslSource(ShaderDataReader datareader, long start, int sourceId) : base(datareader, start) {
            this.sourceId = sourceId;
            offset0 = datareader.ReadInt();
            if (offset0 > 0) {
                arg0 = datareader.ReadInt();
                offset1 = datareader.ReadInt();
                sourcebytes = datareader.ReadBytes(offset1);
            }
            fileId = datareader.ReadBytes(16);
        }
        public string GetStringId() {
            string stringId = ShaderDataReader.BytesToString(fileId);
            stringId = stringId.Replace(" ", "").ToLower();
            return stringId;
        }

    }

}

