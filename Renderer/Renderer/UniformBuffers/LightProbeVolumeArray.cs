using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.Buffers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LightProbeVolume
    {
        public Matrix4x4 WorldToLocalVolumeNormalized;
        public Vector4 BorderMin;
        public Vector4 BorderMax;
        public Vector4 AtlasScale;
        public Vector4 AtlasOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public class LightProbeVolumeArray
    {
        public const int MAX_PROBES = 128;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PROBES)]
        public LightProbeVolume[] Probes = new LightProbeVolume[MAX_PROBES];
    }
}
