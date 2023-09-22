using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderSprites : ParticleFunctionRenderer
    {
        private const string ShaderName = "vrf.particle.sprite";
        private const int VertexSize = 9;

        private Shader shader;
        private readonly VrfGuiContext guiContext;
        private readonly int quadVao;
        private readonly RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly float minSize;
        private readonly float maxSize = 5000f;

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider alphaScale = new LiteralNumberProvider(1f);

        private readonly bool animateInFps;
        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;

        private float[] rawVertices;
        private readonly QuadIndexBuffer quadIndices;
        private int vertexBufferHandle;

        private static bool wireframe;


        public RenderSprites(ParticleDefinitionParser parse, VrfGuiContext vrfGuiContext) : base(parse)
        {
            guiContext = vrfGuiContext;
            shader = vrfGuiContext.ShaderLoader.LoadShader(ShaderName);
            quadIndices = vrfGuiContext.QuadIndices;

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            string textureName = null;

            if (parse.Data.ContainsKey("m_hTexture"))
            {
                textureName = parse.Data.GetProperty<string>("m_hTexture");
            }
            else
            {
                var textures = parse.Array("m_vecTexturesInput");
                if (textures.Length > 0)
                {
                    // TODO: Support more than one texture
                    textureName = textures[0].Data.GetProperty<string>("m_hTexture");
                }
            }

            texture = vrfGuiContext.MaterialLoader.LoadTexture(textureName);

            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);
            overbrightFactor = parse.NumberProvider("m_flOverbrightFactor", overbrightFactor);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            alphaScale = parse.NumberProvider("m_flAlphaScale", alphaScale);
        }

        public override void SetWireframe(bool isWireframe)
        {
            wireframe = isWireframe;
            // Solid color
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        private int SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vertexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);

            var stride = sizeof(float) * VertexSize;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);
            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);
            var uvAttributeLocation = GL.GetAttribLocation(shader.Program, "aTexCoords");
            GL.VertexAttribPointer(uvAttributeLocation, 2, VertexAttribPointerType.Float, false, stride, sizeof(float) * 7);

            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.EnableVertexAttribArray(uvAttributeLocation);

            GL.BindVertexArray(0);

            return vao;
        }

        private void EnsureSpaceForVertices(int count)
        {
            var numFloats = count * VertexSize;

            if (rawVertices == null)
            {
                rawVertices = new float[numFloats];
            }
            else if (rawVertices.Length < numFloats)
            {
                var nextSize = ((count / 64) + 1) * 64 * VertexSize;
                Array.Resize(ref rawVertices, nextSize);
            }
        }

        private void UpdateVertices(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            // Update vertex buffer
            EnsureSpaceForVertices(particles.Count * 4);

            var i = 0;
            foreach (ref var particle in particles.Current)
            {
                var radiusScale = this.radiusScale.NextNumber(ref particle, systemRenderState);

                // Positions
                var modelMatrix = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED
                    ? particle.GetRotationMatrix() * billboardMatrix * particle.GetTransformationMatrix(radiusScale)
                    : particle.GetRotationMatrix() * particle.GetTransformationMatrix(radiusScale);

                var tl = Vector4.Transform(new Vector4(-1, -1, 0, 1), modelMatrix);
                var bl = Vector4.Transform(new Vector4(-1, 1, 0, 1), modelMatrix);
                var br = Vector4.Transform(new Vector4(1, 1, 0, 1), modelMatrix);
                var tr = Vector4.Transform(new Vector4(1, -1, 0, 1), modelMatrix);

                var quadStart = i * VertexSize * 4;
                rawVertices[quadStart + 0] = tl.X;
                rawVertices[quadStart + 1] = tl.Y;
                rawVertices[quadStart + 2] = tl.Z;
                rawVertices[quadStart + (VertexSize * 1) + 0] = bl.X;
                rawVertices[quadStart + (VertexSize * 1) + 1] = bl.Y;
                rawVertices[quadStart + (VertexSize * 1) + 2] = bl.Z;
                rawVertices[quadStart + (VertexSize * 2) + 0] = br.X;
                rawVertices[quadStart + (VertexSize * 2) + 1] = br.Y;
                rawVertices[quadStart + (VertexSize * 2) + 2] = br.Z;
                rawVertices[quadStart + (VertexSize * 3) + 0] = tr.X;
                rawVertices[quadStart + (VertexSize * 3) + 1] = tr.Y;
                rawVertices[quadStart + (VertexSize * 3) + 2] = tr.Z;

                var alphaScale = this.alphaScale.NextNumber(ref particle, systemRenderState);
                // Colors
                for (var j = 0; j < 4; ++j)
                {
                    rawVertices[quadStart + (VertexSize * j) + 3] = particle.Color.X;
                    rawVertices[quadStart + (VertexSize * j) + 4] = particle.Color.Y;
                    rawVertices[quadStart + (VertexSize * j) + 5] = particle.Color.Z;
                    rawVertices[quadStart + (VertexSize * j) + 6] = particle.Alpha * alphaScale;
                }

                // UVs
                var spriteSheetData = texture.SpriteSheetData;
                if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                {
                    var sequence = spriteSheetData.Sequences[particle.Sequence % spriteSheetData.Sequences.Length];

                    var animationTime = animationType switch
                    {
                        ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE => particle.Age,
                        ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                        ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES => particle.Age, // literally dont know what to do with this one
                        _ => particle.Age,
                    };

                    var frameId = 0;

                    if (sequence.Frames.Length > 1)
                    {
                        if (animateInFps)
                        {
                            frameId = (int)Math.Floor(animationRate * animationTime);
                        }
                        else
                        {
                            frameId = (int)Math.Floor(sequence.Frames.Length * animationRate * animationTime);
                        }

                        if (sequence.Clamp)
                        {
                            frameId = Math.Min(frameId, sequence.Frames.Length - 1);
                        }
                        else
                        {
                            frameId %= sequence.Frames.Length;
                        }
                    }

                    var currentFrame = sequence.Frames[frameId];
                    var currentImage = currentFrame.Images[0]; // TODO: Support more than one image per frame?

                    // Lerp frame coords and size
                    var offset = currentImage.UncroppedMin;
                    var scale = currentImage.UncroppedMax - currentImage.UncroppedMin;

                    rawVertices[quadStart + (VertexSize * 0) + 7] = offset.X;
                    rawVertices[quadStart + (VertexSize * 0) + 8] = offset.Y + scale.Y;
                    rawVertices[quadStart + (VertexSize * 1) + 7] = offset.X;
                    rawVertices[quadStart + (VertexSize * 1) + 8] = offset.Y;
                    rawVertices[quadStart + (VertexSize * 2) + 7] = offset.X + scale.X;
                    rawVertices[quadStart + (VertexSize * 2) + 8] = offset.Y;
                    rawVertices[quadStart + (VertexSize * 3) + 7] = offset.X + scale.X;
                    rawVertices[quadStart + (VertexSize * 3) + 8] = offset.Y + scale.Y;
                }
                else
                {
                    rawVertices[quadStart + (VertexSize * 0) + 7] = 0;
                    rawVertices[quadStart + (VertexSize * 0) + 8] = 1;
                    rawVertices[quadStart + (VertexSize * 1) + 7] = 0;
                    rawVertices[quadStart + (VertexSize * 1) + 8] = 0;
                    rawVertices[quadStart + (VertexSize * 2) + 7] = 1;
                    rawVertices[quadStart + (VertexSize * 2) + 8] = 0;
                    rawVertices[quadStart + (VertexSize * 3) + 7] = 1;
                    rawVertices[quadStart + (VertexSize * 3) + 8] = 1;
                }

                i++;
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, particles.Count * VertexSize * 4 * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            // Update vertex buffer
            UpdateVertices(particleBag, systemRenderState, modelViewMatrix);

            // Draw it
            GL.Enable(EnableCap.Blend);

            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else /* if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA) */
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.UseProgram(shader.Program);

            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);

            // set texture unit 0 as uTexture uniform
            shader.SetTexture(0, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            shader.SetUniform1("uOverbrightFactor", overbrightFactor.NextNumber());

            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, quadIndices.GLHandle);

            if (wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }

            // DRAW
            GL.DrawElements(BeginMode.Triangles, particleBag.Count * 6, DrawElementsType.UnsignedShort, 0);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            GL.Enable(EnableCap.CullFace);
            GL.DepthMask(true);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }

            GL.Disable(EnableCap.Blend);

            if (wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
            var parameters = new Dictionary<string, byte>();

            if (renderMode != null && shader.RenderModes.Contains(renderMode))
            {
                parameters.Add($"renderMode_{renderMode}", 1);
            }

            shader = guiContext.ShaderLoader.LoadShader(ShaderName, parameters);
        }
    }
}
