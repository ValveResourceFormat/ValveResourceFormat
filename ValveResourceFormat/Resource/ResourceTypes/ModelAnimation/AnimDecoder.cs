using System;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    internal static class AnimDecoder
    {
        public static int Size(this AnimDecoderType t)
        {
            switch (t)
            {
                case AnimDecoderType.CCompressedFullVector3:
                    return 12;
                case AnimDecoderType.CCompressedStaticVector3:
                case AnimDecoderType.CCompressedAnimVector3:
                case AnimDecoderType.CCompressedAnimQuaternion:
                    return 6;
            }

            return 0;
        }

        public static AnimDecoderType FromString(string s)
        {
            switch (s)
            {
                case "CCompressedReferenceFloat": return AnimDecoderType.CCompressedReferenceFloat;
                case "CCompressedStaticFloat": return AnimDecoderType.CCompressedStaticFloat;
                case "CCompressedFullFloat": return AnimDecoderType.CCompressedFullFloat;
                case "CCompressedReferenceVector3": return AnimDecoderType.CCompressedReferenceVector3;
                case "CCompressedStaticVector3": return AnimDecoderType.CCompressedStaticVector3;
                case "CCompressedStaticFullVector3": return AnimDecoderType.CCompressedStaticFullVector3;
                case "CCompressedAnimVector3": return AnimDecoderType.CCompressedAnimVector3;
                case "CCompressedDeltaVector3": return AnimDecoderType.CCompressedDeltaVector3;
                case "CCompressedFullVector3": return AnimDecoderType.CCompressedFullVector3;
                case "CCompressedReferenceQuaternion": return AnimDecoderType.CCompressedReferenceQuaternion;
                case "CCompressedStaticQuaternion": return AnimDecoderType.CCompressedStaticQuaternion;
                case "CCompressedAnimQuaternion": return AnimDecoderType.CCompressedAnimQuaternion;
                case "CCompressedReferenceInt": return AnimDecoderType.CCompressedReferenceInt;
                case "CCompressedStaticChar": return AnimDecoderType.CCompressedStaticChar;
                case "CCompressedFullChar": return AnimDecoderType.CCompressedFullChar;
                case "CCompressedStaticShort": return AnimDecoderType.CCompressedStaticShort;
                case "CCompressedFullShort": return AnimDecoderType.CCompressedFullShort;
                case "CCompressedStaticInt": return AnimDecoderType.CCompressedStaticInt;
                case "CCompressedFullInt": return AnimDecoderType.CCompressedFullInt;
                case "CCompressedReferenceBool": return AnimDecoderType.CCompressedReferenceBool;
                case "CCompressedStaticBool": return AnimDecoderType.CCompressedStaticBool;
                case "CCompressedFullBool": return AnimDecoderType.CCompressedFullBool;
                case "CCompressedReferenceColor32": return AnimDecoderType.CCompressedReferenceColor32;
                case "CCompressedStaticColor32": return AnimDecoderType.CCompressedStaticColor32;
                case "CCompressedFullColor32": return AnimDecoderType.CCompressedFullColor32;
                case "CCompressedReferenceVector2D": return AnimDecoderType.CCompressedReferenceVector2D;
                case "CCompressedStaticVector2D": return AnimDecoderType.CCompressedStaticVector2D;
                case "CCompressedFullVector2D": return AnimDecoderType.CCompressedFullVector2D;
                case "CCompressedReferenceVector4D": return AnimDecoderType.CCompressedReferenceVector4D;
                case "CCompressedStaticVector4D": return AnimDecoderType.CCompressedStaticVector4D;
                case "CCompressedFullVector4D": return AnimDecoderType.CCompressedFullVector4D;
            }

            return AnimDecoderType.Unknown;
        }
    }
}
