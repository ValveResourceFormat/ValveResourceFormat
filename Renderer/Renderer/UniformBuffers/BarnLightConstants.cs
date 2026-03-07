using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BarnLightConstants
    {
        public const int MAX_BARN_LIGHTS = 320;

        public Matrix4x4 BarnFrustum;
        public Vector4 BarnLightPosition;
        public Vector4 BarnLightDistanceFade_vSkirt;
        public Vector4 BarnLightColor_flCookie;
        public Vector4 BarnLightOrientationQ;
        public Vector3 BarnLightAngleFade;
        private uint _padding0;
        public Vector4 BarnLightShadowOffsetScale;
        public Vector4 BarnLightCookieParameters;
        public Vector4 BarnLightBakedShadowMask;
        public float BarnLightMinRoughness;
        public float BarnLightShadowScale;
        public uint PathTraceIndex_BarnLightFlags;
        private uint _padding1;
        // This is a mat4x3
        public Matrix4x4 BarnIlluminationFromWorld;
    }
}
