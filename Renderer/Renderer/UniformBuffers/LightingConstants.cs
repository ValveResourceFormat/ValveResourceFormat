using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Uniform buffer containing all scene lights and lightmap configuration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class LightingConstants
    {
        /// <summary>Maximum number of dynamic lights supported per scene.</summary>
        public const int MAX_LIGHTS = 256;
        /// <summary>Maximum number of environment map probes supported per scene.</summary>
        public const int MAX_ENVMAPS = 128;

        /// <summary>UV scale applied when sampling the lightmap atlas.</summary>
        public Vector2 LightmapUvScale;
        /// <summary>Non-zero when the current draw is part of the skybox.</summary>
        public uint IsSkybox;
        /// <summary>Number of active barn lights in the scene.</summary>
        public uint NumBarnLights;
        /// <summary>Per-type light counts (index matches light type enum).</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] private readonly uint[] NumLights;
        /// <summary>Sun light baked shadow mask (one-hot per baked shadow channel).</summary>
        public Vector4 SunLightBakedShadowMask;
        /// <summary>World-space position (XYZ) and type (W) for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightPosition_Type;

        /// <summary>World-space direction (XYZ) and inverse range (W) for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightDirection_InvRange;

        /// <summary>Transform matrix from light space to world space for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Matrix4x4[] LightToWorld;

        /// <summary>Linear color (RGB) and brightness (W) for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightColor_Brightness;
        /// <summary>Inner and outer spot cone cosines for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightSpotInnerOuterCosines;
        /// <summary>Falloff curve parameters for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightFallOff;

        /// <summary>Mip level and size constants used when sampling environment maps.</summary>
        public Vector4 EnvMapSizeConstants;

        /// <summary>Gets or sets the number of lightmapped lights in the scene.</summary>
        public uint StaticLightCount { get => NumLights[0]; set => NumLights[0] = value; }

        /// <summary>Gets or sets the number of dynamic lights in the scene.</summary>
        public uint DynamicLightCount { get => NumLights[1]; set => NumLights[1] = value; }

        /// <summary>Initializes a new <see cref="LightingConstants"/> with all arrays allocated to their maximum sizes.</summary>
        public LightingConstants()
        {
            NumLights = new uint[4];
            LightPosition_Type = new Vector4[MAX_LIGHTS];
            LightDirection_InvRange = new Vector4[MAX_LIGHTS];
            LightToWorld = new Matrix4x4[MAX_LIGHTS];
            LightColor_Brightness = new Vector4[MAX_LIGHTS];
            LightSpotInnerOuterCosines = new Vector4[MAX_LIGHTS];
            LightFallOff = new Vector4[MAX_LIGHTS];
        }
    }
}
