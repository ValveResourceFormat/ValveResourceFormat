using System.Runtime.InteropServices;
using OpenTK.Graphics;

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
        public Color4 ClearColor = Color4.Black;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public bool[] FogTypeEnabled;
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
            FogTypeEnabled = new bool[4];
        }
    }
}
