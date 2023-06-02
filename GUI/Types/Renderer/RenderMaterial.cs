using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    public class RenderMaterial
    {
        public Shader Shader => shader;
        public Material Material { get; }
        public Dictionary<string, RenderTexture> Textures { get; } = new();
        public bool IsBlended { get; }
        public bool IsToolsMaterial { get; }

        private readonly Shader shader;
        private readonly bool isAdditiveBlend;
        private readonly bool isRenderBackfaces;
        private readonly bool isOverlay;

        public RenderMaterial(Material material, ShaderLoader shaderLoader)
        {
            Material = material;
            shader = shaderLoader.LoadShader(material.ShaderName, material.GetShaderArguments());

            IsToolsMaterial = material.IntAttributes.ContainsKey("tools.toolsmaterial");
            IsBlended = (material.IntParams.ContainsKey("F_TRANSLUCENT") && material.IntParams["F_TRANSLUCENT"] == 1)
                || material.IntAttributes.ContainsKey("mapbuilder.water")
                || material.IntParams.ContainsKey("F_BLEND_MODE") && material.IntParams["F_BLEND_MODE"] > 0
                || material.ShaderName == "vr_glass.vfx"
                || material.ShaderName == "csgo_effects.vfx"
                || material.ShaderName == "tools_sprite.vfx";
            isAdditiveBlend = material.IntParams.ContainsKey("F_ADDITIVE_BLEND") && material.IntParams["F_ADDITIVE_BLEND"] == 1
                || material.IntParams.ContainsKey("F_BLEND_MODE") && material.IntParams["F_BLEND_MODE"] == 4;
            isRenderBackfaces = material.IntParams.ContainsKey("F_RENDER_BACKFACES") && material.IntParams["F_RENDER_BACKFACES"] == 1;
            isOverlay = (material.IntParams.ContainsKey("F_OVERLAY") && material.IntParams["F_OVERLAY"] == 1)
                || material.IntParams.ContainsKey("F_DEPTH_BIAS") && material.IntParams["F_DEPTH_BIAS"] == 1
                || material.ShaderName.EndsWith("static_overlay.vfx", System.StringComparison.Ordinal);
        }

        public void Render(Shader shader = default)
        {
            //Start at 1, texture unit 0 is reserved for the animation texture
            var textureUnit = 1;
            int uniformLocation;

            shader ??= this.shader;

            foreach (var (name, texture) in Textures)
            {
                uniformLocation = shader.GetUniformLocation(name);

                if (uniformLocation > -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
                    GL.BindTexture(texture.Target, texture.Handle);
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

            if (IsBlended)
            {
                GL.DepthMask(false);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, isAdditiveBlend ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
            }

            if (isOverlay)
            {
                GL.Enable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffset(-0.05f, -64);
            }

            if (isRenderBackfaces)
            {
                GL.Disable(EnableCap.CullFace);
            }
        }

        public void PostRender()
        {
            if (IsBlended)
            {
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }

            if (isOverlay)
            {
                GL.Disable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffset(0, 0);
            }

            if (isRenderBackfaces)
            {
                GL.Enable(EnableCap.CullFace);
            }
        }
    }
}
