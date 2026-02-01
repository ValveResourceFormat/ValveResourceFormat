using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Per-volume light probe sampling parameters for shader uniform buffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightProbeVolume
    {
        public Matrix4x4 WorldToLocalVolumeNormalized;
        public Vector4 BorderMin;
        public Vector4 BorderMax;
        public Vector4 AtlasScale;
        public Vector4 AtlasOffset;
    }

    /// <summary>
    /// Uniform buffer array containing all light probe volume data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class LightProbeVolumeArray
    {
        public const int MAX_PROBES = 128;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PROBES)]
        public LightProbeVolume[] Probes = new LightProbeVolume[MAX_PROBES];
    }
}
