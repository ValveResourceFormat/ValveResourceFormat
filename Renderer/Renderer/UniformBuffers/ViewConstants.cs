using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Uniform buffer containing camera transforms, fog, and per-frame view state.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public class ViewConstants
    {
        /// <summary>A view remapped against itself: unit scale, no bias.</summary>
        public static readonly Vector4 PixelRemapIdentity = new(1f, 1f, 0f, 0f);

        /// <summary>Gets the distance to the near plane this view was projected with.</summary>
        /// <remarks>
        /// Recovered from the projection rather than carried as a field, so it costs nothing in the layout
        /// this class is marshalled to the GPU with. The reverse Z form
        /// <see cref="Camera.CreateProjectionMatrix"/> builds has <c>M33 = near / (far - near)</c> and
        /// <c>M43 = near * far / (far - near)</c>, which inverts to this exactly and collapses to plain
        /// <c>M43</c> when the far plane is infinite.
        /// </remarks>
        public float NearPlane => ViewToProjection.M43 / (1f + ViewToProjection.M33);

        /// <summary>Gets the far plane distance, or <see cref="float.PositiveInfinity"/> when there is none.</summary>
        /// <remarks>See <see cref="NearPlane"/> for where these come out of the matrix.</remarks>
        public float FarPlane => ViewToProjection.M33 != 0f
            ? ViewToProjection.M43 / ViewToProjection.M33
            : float.PositiveInfinity;

        /// <summary>Combined world-to-clip transform (view * projection).</summary>
        public Matrix4x4 WorldToProjection = Matrix4x4.Identity;
        /// <summary>Inverse of <see cref="WorldToProjection"/>, mapping clip space back to world space.</summary>
        public Matrix4x4 ProjectionToWorld = Matrix4x4.Identity;
        /// <summary>View matrix transforming world-space positions into camera space.</summary>
        public Matrix4x4 WorldToView = Matrix4x4.Identity;
        /// <summary>Projection matrix transforming camera-space positions into clip space.</summary>
        public Matrix4x4 ViewToProjection = Matrix4x4.Identity;
        /// <summary>Third row of the inverse projection matrix, used for depth linearization.</summary>
        public Vector4 InvProjRow3 = Vector4.Zero;
        /// <summary>Reciprocal of the viewport dimensions in pixels.</summary>
        public Vector2 InvViewportSize;
        /// <summary>Viewport dimensions in pixels.</summary>
        public Vector2 ViewportSize;
        /// <summary>World-space position of the camera.</summary>
        public Vector3 CameraPosition = Vector3.Zero;
        /// <summary>Minimum window-space depth of the viewport depth range (the far plane under reverse-Z).</summary>
        public float ViewportMinZ;
        /// <summary>World-space forward direction of the camera.</summary>
        public Vector3 CameraDirWs;
        /// <summary>Maximum window-space depth of the viewport depth range (the near plane under reverse-Z).</summary>
        public float ViewportMaxZ;
        /// <summary>World-space up direction of the camera.</summary>
        public Vector3 CameraUpDirWs;
        /// <summary>Current scene time in seconds, used for animated effects.</summary>
        public float Time;
        /// <summary>Transform matrix from world space into shadow map texture space, for the first sun cascade. Forms <c>g_matWorldToShadow[0]</c> in the shader.</summary>
        public Matrix4x4 WorldToShadow = Matrix4x4.Identity;
        /// <summary>Transform into the second, wider sun shadow cascade. Forms <c>g_matWorldToShadow[1]</c>.</summary>
        public Matrix4x4 WorldToShadowCascade1 = Matrix4x4.Identity;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public Vector2 _ViewPadding1;
        /// <summary>Depth bias applied when sampling the sun light shadow map.</summary>
        public float SunLightShadowBias = 0.001f;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public float _ViewPadding2;

        /// <summary>When <see langword="true"/>, volumetric fog is active and evaluated in the shader.</summary>
        public bool VolumetricFogActive;
        /// <summary>When <see langword="true"/>, height-based gradient fog is active.</summary>
        public bool GradientFogActive;
        /// <summary>When <see langword="true"/>, cube-mapped sky fog is active.</summary>
        public bool CubeFogActive;
        /// <summary>Active render mode override; 0 means normal shading.</summary>
        public int RenderMode;
        /// <summary>Bias and scale applied to the gradient fog density.</summary>
        public Vector4 GradientFogBiasAndScale;
        /// <summary>Color (RGB) and maximum opacity (A) for gradient fog.</summary>
        public Vector4 GradientFogColor_Opacity;
        /// <summary>Horizontal and vertical density exponents for gradient fog.</summary>
        public Vector2 GradientFogExponents;
        /// <summary>Near/far culling distances for gradient fog evaluation.</summary>
        public Vector2 GradientFogCullingParams;
        /// <summary>Cube fog offset, scale, bias, and exponent parameters.</summary>
        public Vector4 CubeFog_Offset_Scale_Bias_Exponent;
        /// <summary>Cube fog height offset, scale, exponent, and log2 mip parameters.</summary>
        public Vector4 CubeFog_Height_Offset_Scale_Exponent_Log2Mip;
        /// <summary>Transform from world space to the cube fog sky's object space.</summary>
        public Matrix4x4 CubeFogSkyWsToOs;
        /// <summary>Cube fog culling parameters, exposure bias, and maximum opacity.</summary>
        public Vector4 CubeFogCullingParams_ExposureBias_MaxOpacity;

        /// <summary>World-to-clip transform from when the depth pyramid was last generated, used for GPU occlusion culling.</summary>
        public Matrix4x4 WorldToProjectionPrev = Matrix4x4.Identity;

        /// <summary>Wetness coverage, drying amount, rain strength and puddle ripple strength.</summary>
        public Vector4 EnvWetness = new(1f, 0f, 1f, 1f);

        /// <summary>Puddle ripple direction in X, over 0 to 1 for a full turn.</summary>
        public Vector4 EnvWetnessRipple;

        /// <summary>Initializes a new <see cref="ViewConstants"/> with identity matrices and default values.</summary>
        public ViewConstants()
        {
        }
    }
}
