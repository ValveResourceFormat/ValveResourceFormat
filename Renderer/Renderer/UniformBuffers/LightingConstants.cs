using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    [StructLayout(LayoutKind.Sequential)]
    public class LightingConstants
    {
        public const int MAX_LIGHTS = 256;
        public const int MAX_ENVMAPS = 128;

        public Vector2 LightmapUvScale;
        public uint IsSkybox;
        public float _LightingPadding1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public uint[] NumLights;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public uint[] NumLightsBakedShadowIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightPosition_Type;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightDirection_InvRange;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Matrix4x4[] LightToWorld;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightColor_Brightness;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightSpotInnerOuterCosines;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightFallOff;

        public Vector4 EnvMapSizeConstants;

        public LightingConstants()
        {
            NumLights = new uint[4];
            NumLightsBakedShadowIndex = new uint[4];
            LightPosition_Type = new Vector4[MAX_LIGHTS];
            LightDirection_InvRange = new Vector4[MAX_LIGHTS];
            LightToWorld = new Matrix4x4[MAX_LIGHTS];
            LightColor_Brightness = new Vector4[MAX_LIGHTS];
            LightSpotInnerOuterCosines = new Vector4[MAX_LIGHTS];
            LightFallOff = new Vector4[MAX_LIGHTS];
        }
    }
}
