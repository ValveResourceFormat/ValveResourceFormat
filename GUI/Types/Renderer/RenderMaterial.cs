using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    public class RenderMaterial
    {
        public Material Material { get; }
        public Dictionary<string, int> Textures { get; } = new Dictionary<string, int>();
        public bool IsBlended => isTranslucent;

        private readonly float flAlphaTestReference;
        private readonly bool isTranslucent;
        private readonly bool isAdditiveBlend;
        private readonly bool isRenderBackfaces;

        public RenderMaterial(Material material)
        {
            Material = material;

            if (material.IntParams.ContainsKey("F_ALPHA_TEST") &&
                material.IntParams["F_ALPHA_TEST"] == 1 &&
                material.FloatParams.ContainsKey("g_flAlphaTestReference"))
            {
                flAlphaTestReference = material.FloatParams["g_flAlphaTestReference"];
            }

            isTranslucent = (material.IntParams.ContainsKey("F_TRANSLUCENT") && material.IntParams["F_TRANSLUCENT"] == 1) || material.IntAttributes.ContainsKey("mapbuilder.water") || material.ShaderName == "vr_glass.vfx";
            isAdditiveBlend = material.IntParams.ContainsKey("F_ADDITIVE_BLEND") && material.IntParams["F_ADDITIVE_BLEND"] == 1;
            isRenderBackfaces = material.IntParams.ContainsKey("F_RENDER_BACKFACES") && material.IntParams["F_RENDER_BACKFACES"] == 1;
        }

        public void Render(Shader shader)
        {
            //Start at 1, texture unit 0 is reserved for the animation texture
            var textureUnit = 1;
            int uniformLocation;

            foreach (var texture in Textures)
            {
                uniformLocation = shader.GetUniformLocation(texture.Key);

                if (uniformLocation > -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
                    GL.BindTexture(TextureTarget.Texture2D, texture.Value);
                    GL.Uniform1(uniformLocation, textureUnit);

                    textureUnit++;
                }
            }

            foreach (var param in Material.FloatParams)
            {
                uniformLocation = shader.GetUniformLocation(param.Key);

                if (uniformLocation > -1)
                {
                    GL.Uniform1(uniformLocation, param.Value);
                }
            }

            foreach (var param in Material.VectorParams)
            {
                uniformLocation = shader.GetUniformLocation(param.Key);

                if (uniformLocation > -1)
                {
                    GL.Uniform4(uniformLocation, new Vector4(param.Value.X, param.Value.Y, param.Value.Z, param.Value.W));
                }
            }

            var alphaReference = shader.GetUniformLocation("g_flAlphaTestReference");

            if (alphaReference > -1)
            {
                GL.Uniform1(alphaReference, flAlphaTestReference);
            }

            if (isTranslucent)
            {
                GL.DepthMask(false);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, isAdditiveBlend ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
            }

            if (isRenderBackfaces)
            {
                GL.Disable(EnableCap.CullFace);
            }
        }

        public void PostRender()
        {
            if (isTranslucent)
            {
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }

            if (isRenderBackfaces)
            {
                GL.Enable(EnableCap.CullFace);
            }
        }
    }
}
