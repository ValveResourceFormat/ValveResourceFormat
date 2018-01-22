using System;

namespace ValveResourceFormat.ResourceTypes.Animation
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
}
