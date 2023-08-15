using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.UniformBuffers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewConstants
    {
        public Matrix4x4 ViewToProjection;
        public Matrix4x4 WorldToProjection;
        public Matrix4x4 WorldToView;
        public Vector3 CameraPosition;
        public float Time;
        public Vector4 GradientFogBiasAndScale;
        public Vector4 GradientFogColor_Opacity;
        public Vector2 GradientFogExponents;
        public Vector2 GradientFogCullingParams;
        public Vector4 CubeFog_Offset_Scale_Bias_Exponent;
        public Vector4 CubeFog_Height_Offset_Scale_Exponent_Log2Mip;
        public Matrix4x4 CubeFogSkyWsToOs;
        public Vector4 CubeFogCullingParams_ExposureBias_MaxOpacity;
    }
}
