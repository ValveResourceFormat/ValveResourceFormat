using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Uniform buffer containing all scene lights and lightmap configuration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class LightingConstants
    {
        /// <summary>Maximum number of lights (static plus dynamic) supported per scene.</summary>
        public const int MAX_LIGHTS = 256;

        /// <summary>Maximum number of per-pixel lights with a shadow map, as many as the games light per view.</summary>
        public const int MAX_SHADOWED_DYNAMIC_LIGHTS = 16;

        /// <summary>UV scale applied when sampling the lightmap atlas.</summary>
        public Vector2 LightmapUvScale;
        /// <summary>Non-zero to shade reflections with each environment map's debug colour instead of its texture.</summary>
        public uint EnvMapDebugColors;
        /// <summary>Number of active barn lights in the scene.</summary>
        public uint NumBarnLights;
        /// <summary>Per-type light counts (index matches light type enum).</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] private readonly uint[] NumLights;
        /// <summary>
        /// Surface-to-sun direction (XYZ) of the environment light, or zero when the scene has none, and the roughness its
        /// specular highlight is no sharper than (W).
        /// </summary>
        public Vector4 SunDirection;
        /// <summary>Sun color: linear color premultiplied by brightness (RGB), render-specular flag (W).</summary>
        public Vector4 SunColor;
        /// <summary>Sun baked shadow data: V2 stores the one-hot shadow channel mask, V1 stores the sun's baked light index 0-255 in X (-1 for none).</summary>
        public Vector4 SunLightBakedShadowMask;
        /// <summary>World-space position (XYZ) and type (W) for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightPosition_Type;

        /// <summary>World-space direction (XYZ) and inverse range (W) for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightDirection_InvRange;

        /// <summary>Transform matrix from light space to world space for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Matrix4x4[] LightToWorld;

        /// <summary>Light color per light. V1: premultiplied linear color (RGB) and render-specular flag (W), like <c>g_vBakedLightColor</c>. V2: linear color (RGB) and brightness (W).</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightColor_Brightness;
        /// <summary>Spot cone cosines or ortho half extents (XY), plus the diffuse and transmissive render gates (ZW), for each light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightSpotInnerOuterCosines;
        /// <summary>Per-light falloff shaped like <c>g_vSingleLightFalloffParams</c>: linear (X) and quadratic (Y) attenuation, squared range cutoff (Z), zero-at-range bias (W).</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightFallOff;
        /// <summary>
        /// Per-light shadow and cookie data: the slot in <see cref="LightWorldToShadow"/> and <see cref="LightShadowOffsetScale"/> (X), -1 for none,
        /// the slice of the light cookie volume (Y), -1 for none, and the reciprocal tangent of the outer cone angle (Z).
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LIGHTS)] public Vector4[] LightShadowParams;
        /// <summary>Transform from world space to the shadow frustum of each shadowed dynamic light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_SHADOWED_DYNAMIC_LIGHTS)] public Matrix4x4[] LightWorldToShadow;
        /// <summary>Shadow atlas offset (XY) and scale (ZW) of each shadowed dynamic light.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_SHADOWED_DYNAMIC_LIGHTS)] public Vector4[] LightShadowOffsetScale;

        /// <summary>Environment map constants: highest mip level (X), number of cubes (Y), RGBM encoding bool (Z).</summary>
        public Vector4 EnvMapSizeConstants;

        /// <summary>Red channel of the ambient lighting: weights against the normal (XYZ) and a constant (W).</summary>
        public Vector4 AmbientLightingSHR;
        /// <summary>Green channel of the ambient lighting.</summary>
        public Vector4 AmbientLightingSHG;
        /// <summary>Blue channel of the ambient lighting.</summary>
        public Vector4 AmbientLightingSHB;

        /// <summary>Direction the directional ambient lighting is facing (XYZ), and how much the sun shadow darkens it (W).</summary>
        public Vector4 AmbientDirection_ShadowAmount;
        /// <summary>Linear color of the directional ambient lighting multiplied by its intensity, zero when there is none.</summary>
        public Vector4 AmbientDirectionColor;

        /// <summary>Direction the specular light travels (XYZ), and the exponent of its highlight (W).</summary>
        public Vector4 SpecularDirection_Power;
        /// <summary>Linear specular color whose highlight is added to the albedo before it is lit, zero when there is none.</summary>
        public Vector4 SpecularAlbedoColor;
        /// <summary>Linear specular color whose highlight is added after lighting where the sun reaches, zero when there is none.</summary>
        public Vector4 SpecularLitColor;

        /// <summary>Gets or sets the number of lightmapped lights in the scene.</summary>
        public uint StaticLightCount { get => NumLights[0]; set => NumLights[0] = value; }

        /// <summary>Gets or sets the total light count (static plus dynamic); dynamic lights occupy indices from <see cref="StaticLightCount"/> up to this value.</summary>
        public uint DynamicLightCount { get => NumLights[1]; set => NumLights[1] = value; }

        /// <summary>Initializes a new <see cref="LightingConstants"/> with all arrays allocated to their maximum sizes.</summary>
        public LightingConstants()
        {
            NumLights = new uint[4];
            SunLightBakedShadowMask = new Vector4(-1f, 0f, 0f, 0f);
            LightPosition_Type = new Vector4[MAX_LIGHTS];
            LightDirection_InvRange = new Vector4[MAX_LIGHTS];
            LightToWorld = new Matrix4x4[MAX_LIGHTS];
            LightColor_Brightness = new Vector4[MAX_LIGHTS];
            LightSpotInnerOuterCosines = new Vector4[MAX_LIGHTS];
            LightFallOff = new Vector4[MAX_LIGHTS];
            LightShadowParams = new Vector4[MAX_LIGHTS];
            Array.Fill(LightShadowParams, new Vector4(-1f, -1f, 0f, 0f));
            LightWorldToShadow = new Matrix4x4[MAX_SHADOWED_DYNAMIC_LIGHTS];
            LightShadowOffsetScale = new Vector4[MAX_SHADOWED_DYNAMIC_LIGHTS];
        }
    }
}
