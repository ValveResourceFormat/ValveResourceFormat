using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.UniformBuffers
{
    [StructLayout(LayoutKind.Sequential)]
    public class LightingConstants
    {
        public const int MAX_ENVMAPS = 144;

        public Vector4 LightmapUvScale;
        public Matrix4x4 SunLightPosition;
        public Vector4 SunLightColor;
        public Vector4 EnvMapSizeConstants;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Matrix4x4[] EnvMapWorldToLocal;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapBoxMins;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapBoxMaxs;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapEdgeInvEdgeWidth;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapProxySphere;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapColorRotated;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly Vector4[] EnvMapNormalizationSH;

        public LightingConstants()
        {
            EnvMapWorldToLocal = new Matrix4x4[MAX_ENVMAPS];
            EnvMapBoxMins = new Vector4[MAX_ENVMAPS];
            EnvMapBoxMaxs = new Vector4[MAX_ENVMAPS];
            EnvMapEdgeInvEdgeWidth = new Vector4[MAX_ENVMAPS];
            EnvMapProxySphere = new Vector4[MAX_ENVMAPS];
            EnvMapColorRotated = new Vector4[MAX_ENVMAPS];
            EnvMapNormalizationSH = new Vector4[MAX_ENVMAPS];
        }
    }
}
