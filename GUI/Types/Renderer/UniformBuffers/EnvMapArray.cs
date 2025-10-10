using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.Buffers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EnvMapData
    {
        public Matrix4x4 WorldToLocal;
        public Vector3 BoxMins;
        public uint ArrayIndex;
        public Vector3 BoxMaxs;
        public uint Padding1;
        public Vector4 InvEdgeWidth;
        public Vector3 Origin;
        public uint ProjectionType;
        public Vector3 Color;
        public uint AssociatedLPV;
        public Vector4 NormalizationSH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public class EnvMapArray
    {
        public const int MAX_ENVMAPS = 128;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly EnvMapData[] EnvMaps = new EnvMapData[MAX_ENVMAPS];
    }
}
