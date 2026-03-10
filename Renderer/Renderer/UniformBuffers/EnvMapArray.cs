using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Per-probe reflection data for shader uniform buffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnvMapData
    {
        /// <summary>Transform matrix from world space to the probe's local space.</summary>
        public Matrix4x4 WorldToLocal;
        /// <summary>Minimum corner of the probe's influence box in local space.</summary>
        public Vector3 BoxMins;
        /// <summary>Index of this probe's cubemap in the texture array.</summary>
        public uint ArrayIndex;
        /// <summary>Maximum corner of the probe's influence box in local space.</summary>
        public Vector3 BoxMaxs;
        /// <summary>Padding to satisfy alignment requirements.</summary>
        public uint Padding1;
        /// <summary>Inverse edge width for box projection blend falloff.</summary>
        public Vector4 InvEdgeWidth;
        /// <summary>World-space origin of the environment map probe.</summary>
        public Vector3 Origin;
        /// <summary>Projection type identifier (e.g. sphere or box).</summary>
        public uint ProjectionType;
        /// <summary>Tint color applied to the environment map.</summary>
        public Vector3 Color;
        /// <summary>Index of the associated light probe volume.</summary>
        public uint AssociatedLPV;
        /// <summary>Spherical harmonics normalization coefficients.</summary>
        public Vector4 NormalizationSH;
    }

    /// <summary>
    /// Uniform buffer array containing all environment map probe data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class EnvMapArray
    {
        /// <summary>Maximum number of environment map probes supported per scene.</summary>
        public const int MAX_ENVMAPS = 128;

        /// <summary>Array of environment map probe data entries.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ENVMAPS)]
        public readonly EnvMapData[] EnvMaps = new EnvMapData[MAX_ENVMAPS];
    }
}
