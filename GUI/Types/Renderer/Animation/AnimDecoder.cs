using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.Renderer.Animation
{
    public enum AnimDecoderType
    {
        Ignore,
        CCompressedStaticFullVector3,
        CCompressedFullVector3,
        CCompressedDeltaVector3,
        CCompressedAnimVector3,
        CCompressedStaticVector,
        CCompressedAnimQuaternion,
    }

    internal static class AnimDecoder
    {
        public static int Size(this AnimDecoderType t) {
            switch (t)
            {
                case AnimDecoderType.CCompressedFullVector3:
                case AnimDecoderType.CCompressedAnimVector3:
                    return 12;
                case AnimDecoderType.CCompressedStaticVector:
                case AnimDecoderType.CCompressedAnimQuaternion:
                    return 6;
            }

            return 0;
        }

        public static AnimDecoderType FromString(string s)
        {
            switch (s)
            {
                case "CCompressedStaticFullVector3":
                    return AnimDecoderType.CCompressedStaticFullVector3;
                case "CCompressedFullVector3":
                    return AnimDecoderType.CCompressedFullVector3;
                case "CCompressedDeltaVector3":
                    return AnimDecoderType.CCompressedDeltaVector3;
                case "CCompressedAnimVector3":
                    return AnimDecoderType.CCompressedAnimVector3;
                case "CCompressedStaticVector":
                    return AnimDecoderType.CCompressedStaticVector;
                case "CCompressedAnimQuaternion":
                    return AnimDecoderType.CCompressedAnimQuaternion;
            }

            return AnimDecoderType.Ignore;
        }
    }
}
