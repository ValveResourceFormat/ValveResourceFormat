using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>GPU struct holding parameters for a single barn light.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BarnLightConstants
    {
        /// <summary>Maximum number of barn lights that can be active in a scene.</summary>
        public const int MAX_BARN_LIGHTS = 128; // 320 would be the max lights in a scene

        /// <summary>Frustum matrix used for barn light shadow projection.</summary>
        public Matrix4x4 BarnFrustum;
        /// <summary>World-space position of the barn light.</summary>
        public Vector4 BarnLightPosition;
        /// <summary>Distance fade parameters and skirt value for the barn light.</summary>
        public Vector4 BarnLightDistanceFade_vSkirt;
        /// <summary>Color and cookie blend factor for the barn light.</summary>
        public Vector4 BarnLightColor_flCookie;
        /// <summary>Orientation quaternion of the barn light.</summary>
        public Vector4 BarnLightOrientationQ;
        /// <summary>Angle-based fade parameters for the barn light.</summary>
        public Vector3 BarnLightAngleFade;
        private uint _padding0;
        /// <summary>Shadow map offset and scale for the barn light.</summary>
        public Vector4 BarnLightShadowOffsetScale;
        /// <summary>Cookie texture parameters for the barn light.</summary>
        public Vector4 BarnLightCookieParameters;
        /// <summary>Baked shadow mask values for the barn light.</summary>
        public Vector4 BarnLightBakedShadowMask;
        /// <summary>Minimum roughness value clamped for this barn light.</summary>
        public float BarnLightMinRoughness;
        /// <summary>Shadow intensity scale for the barn light.</summary>
        public float BarnLightShadowScale;
        /// <summary>Packed path trace index and barn light flags.</summary>
        public uint PathTraceIndex_BarnLightFlags;
        private uint _padding1;
        /// <summary>Transform matrix from world space to barn light illumination space.</summary>
        public OpenTK.Mathematics.Matrix3x4 BarnIlluminationFromWorld;
    }
}
