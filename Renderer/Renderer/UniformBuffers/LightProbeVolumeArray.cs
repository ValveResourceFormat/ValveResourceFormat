using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Per-volume light probe sampling parameters for shader uniform buffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightProbeVolume
    {
        /// <summary>Transform matrix from world space to normalized local volume space.</summary>
        public Matrix4x4 WorldToLocalVolumeNormalized;
        /// <summary>Minimum border margin in local volume space for trilinear blending.</summary>
        public Vector4 BorderMin;
        /// <summary>Maximum border margin in local volume space for trilinear blending.</summary>
        public Vector4 BorderMax;
        /// <summary>Scale applied when sampling this volume from the probe atlas.</summary>
        public Vector4 AtlasScale;
        /// <summary>Offset applied when sampling this volume from the probe atlas.</summary>
        public Vector4 AtlasOffset;
    }

    /// <summary>
    /// Uniform buffer array containing all light probe volume data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class LightProbeVolumeArray
    {
        /// <summary>Maximum number of light probe volumes supported per scene.</summary>
        public const int MAX_PROBES = 128;

        /// <summary>Array of light probe volume data entries.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PROBES)]
        public LightProbeVolume[] Probes = new LightProbeVolume[MAX_PROBES];
    }
}
