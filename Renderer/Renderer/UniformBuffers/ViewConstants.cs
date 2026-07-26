using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Uniform buffer containing camera transforms, fog, and per-frame view state.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public class ViewConstants
    {
        /// <summary>A view remapped against itself: unit scale, no bias. Statics are not part of the layout.</summary>
        public static readonly Vector4 PixelRemapIdentity = new(1f, 1f, 0f, 0f);

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
        /// <summary>Transform matrix from world space into shadow map texture space.</summary>
        public Matrix4x4 WorldToShadow = Matrix4x4.Identity;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public Vector2 _ViewPadding1;
        /// <summary>Depth bias applied when sampling the sun light shadow map.</summary>
        public float SunLightShadowBias = 0.001f;
        /// <summary>When <see langword="true"/>, experimental dynamic lighting is evaluated for this frame.</summary>
        public bool ExperimentalLightsEnabled;

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

        /// <summary>Index of the first word of the screen tile mask region in the light cull bit array.</summary>
        public uint LightTileBase;
        /// <summary>Index of the first word of the depth slice mask region in the light cull bit array.</summary>
        public uint LightSliceBase;
        /// <summary>Number of 32 bit words each tile and slice mask occupies this frame.</summary>
        public uint LightCullWords;
        /// <summary>When <see langword="true"/>, the tile masks were built for this view and may be used.</summary>
        public bool LightTilesValid;

        /// <summary>Right shift converting a pixel coordinate into a tile coordinate.</summary>
        public uint LightTileShift;
        /// <summary>Number of tile columns.</summary>
        public uint LightTileCols;
        /// <summary>Number of tile rows.</summary>
        public uint LightTileRows;
        /// <summary>Number of depth slices in the slice mask region.</summary>
        public uint LightSliceCount;

        /// <summary>Logarithmic depth slice mapping applied to camera forward distance: X scale, Y bias.</summary>
        public Vector4 LightDepthSliceParams;

        /// <summary>
        /// World-to-clip transform of the camera the light masks were built for. Only the world space
        /// lookup needs it; a raster pass finds its tile through <see cref="LightCullPixelRemap"/>.
        /// </summary>
        public Matrix4x4 LightCullWorldToProjection = Matrix4x4.Identity;

        /// <summary>
        /// Maps this pass's pixel to the pixel the same ray occupies in the view the light masks were
        /// built for: xy scale, zw bias. Identity for the view that produced them.
        /// </summary>
        /// <remarks>
        /// Only correct while this pass and that view share an eye position and an orientation. A pass
        /// drawn from an eye of its own has to reach for the world space lookup instead.
        /// </remarks>
        /// <seealso cref="Camera.GetPixelRemapTo"/>
        public Vector4 LightCullPixelRemap = PixelRemapIdentity;

        /// <summary>World-space position of the camera the light masks were built for.</summary>
        public Vector3 LightCullCameraPosition;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public float _LightCullPadding0;

        /// <summary>World-space forward direction of the camera the light masks were built for.</summary>
        public Vector3 LightCullCameraDir;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public float _LightCullPadding1;

        /// <summary>Index of the first word of the env map screen tile mask region.</summary>
        public uint EnvMapTileBase;
        /// <summary>Index of the first word of the env map depth bin mask region.</summary>
        public uint EnvMapBinBase;
        /// <summary>
        /// Number of 32 bit words each env map tile and bin mask occupies this frame. Zero when the scene
        /// has no env maps, which is also what makes the masks unusable and selects the fallback.
        /// </summary>
        public uint EnvMapCullWords;
        /// <summary>Number of env maps packed into the env map array this frame.</summary>
        public uint NumEnvMaps;

        /// <summary>Initializes a new <see cref="ViewConstants"/> with identity matrices and default values.</summary>
        public ViewConstants()
        {
        }
    }
}
