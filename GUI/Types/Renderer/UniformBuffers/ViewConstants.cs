using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.UniformBuffers
{
    [StructLayout(LayoutKind.Sequential)]
    public class ViewConstants
    {
        public Matrix4x4 ViewToProjection = Matrix4x4.Identity;
        public Matrix4x4 WorldToProjection = Matrix4x4.Identity;
        public Matrix4x4 WorldToView = Matrix4x4.Identity;
        public Vector3 CameraPosition = Vector3.Zero;
        public float Time;
        public Matrix4x4 WorldToShadow = Matrix4x4.Identity;
        public Vector2 _ViewPadding1;
        public float SunLightShadowBias = 0.001f;
        public bool ExperimentalLightsEnabled;

        public bool VolumetricFogActive;
        public bool GradientFogActive;
        public bool CubeFogActive;
        public int RenderMode;
        public Vector4 GradientFogBiasAndScale;
        public Vector4 GradientFogColor_Opacity;
        public Vector2 GradientFogExponents;
        public Vector2 GradientFogCullingParams;
        public Vector4 CubeFog_Offset_Scale_Bias_Exponent;
        public Vector4 CubeFog_Height_Offset_Scale_Exponent_Log2Mip;
        public Matrix4x4 CubeFogSkyWsToOs;
        public Vector4 CubeFogCullingParams_ExposureBias_MaxOpacity;

        public ViewConstants()
        {
        }
    }
}
