using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    static class MeshBatchRenderer
    {
        public struct Request
        {
            public Matrix4x4 Transform;
            public RenderableMesh Mesh;
            public DrawCall Call;
            public float DistanceFromCamera;
            public int RenderOrder;
            public SceneNode Node;
        }

        public static int ComparePipeline(Request a, Request b)
        {
            if (a.Call.Material.Shader.Program == b.Call.Material.Shader.Program)
            {
                return a.Call.Material.SortId - b.Call.Material.SortId;
            }

            return a.Call.Material.Shader.Program - b.Call.Material.Shader.Program;
        }

        public static int CompareRenderOrderThenPipeline(Request a, Request b)
        {
            if (a.RenderOrder == b.RenderOrder)
            {
                return ComparePipeline(a, b);
            }

            return a.RenderOrder - b.RenderOrder;
        }

        public static int CompareCameraDistance(Request a, Request b)
        {
            return -a.DistanceFromCamera.CompareTo(b.DistanceFromCamera);
        }

        public static void Render(List<Request> requests, Scene.RenderContext context)
        {
            if (context.RenderPass == RenderPass.Opaque)
            {
                requests.Sort(ComparePipeline);
            }
            else if (context.RenderPass == RenderPass.StaticOverlay)
            {
                requests.Sort(CompareRenderOrderThenPipeline);
            }
            else if (context.RenderPass == RenderPass.Translucent)
            {
                requests.Sort(CompareCameraDistance);
            }

            DrawBatch(requests, context);
        }

        private ref struct Uniforms
        {
            public int Animated;
            public int AnimationTexture;
            public int NumBones;
            public int Transform;
            public int Tint;
            public int ObjectId;
            public int MeshId;
            public int ShaderId;
            public int ShaderProgramId;
            public int CubeMapArrayIndices;
            public int MorphCompositeTexture;
            public int MorphCompositeTextureSize;
            public int MorphVertexIdOffset;
        }

        private static void DrawBatch(List<Request> requests, Scene.RenderContext context)
        {
            uint vao = 0;
            Shader shader = null;
            RenderMaterial material = null;
            Uniforms uniforms = new();

            foreach (var request in requests)
            {
                if (!context.Scene.ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
                {
                    continue;
                }

                if (vao != request.Call.VertexArrayObject)
                {
                    vao = request.Call.VertexArrayObject;
                    GL.BindVertexArray(vao);
                }

                if (material != request.Call.Material)
                {
                    material?.PostRender();

                    var requestShader = context.ReplacementShader ?? request.Call.Material.Shader;

                    // If the material did not change, shader could not have changed
                    if (shader != requestShader)
                    {
                        shader = requestShader;
                        uniforms = new Uniforms
                        {
                            Animated = shader.GetUniformLocation("bAnimated"),
                            AnimationTexture = shader.GetUniformLocation("animationTexture"),
                            NumBones = shader.GetUniformLocation("fNumBones"),
                            Transform = shader.GetUniformLocation("transform"),
                            Tint = shader.GetUniformLocation("vTint"),
                            CubeMapArrayIndices = shader.GetUniformLocation("g_iEnvMapArrayIndices"),
                            ObjectId = shader.GetUniformLocation("sceneObjectId"),
                            MeshId = shader.GetUniformLocation("meshId"),
                            ShaderId = shader.GetUniformLocation("shaderId"),
                            ShaderProgramId = shader.GetUniformLocation("shaderProgramId"),
                            MorphCompositeTexture = shader.GetUniformLocation("morphCompositeTexture"),
                            MorphCompositeTextureSize = shader.GetUniformLocation("morphCompositeTextureSize"),
                            MorphVertexIdOffset = shader.GetUniformLocation("morphVertexIdOffset")
                        };

                        GL.UseProgram(shader.Program);

                        foreach (var buffer in context.View.Buffers)
                        {
                            buffer.SetBlockBinding(shader);
                        }

                        foreach (var ((Slot, Name), Texture) in context.View.Textures)
                        {
                            shader.SetTexture((int)Slot, Name, Texture);
                        }

                        context.Scene.LightingInfo.SetLightmapTextures(shader);
                        context.Scene.FogInfo.SetCubemapFogTexture(shader);
                    }

                    material = request.Call.Material;
                    material.Render(shader);
                }

                Draw(ref uniforms, request);
            }

            material?.PostRender();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(ref Uniforms uniforms, Request request)
        {
            var transformTk = request.Transform.ToOpenTK();
            GL.UniformMatrix4(uniforms.Transform, false, ref transformTk);

            #region Picking
            if (uniforms.ObjectId != -1)
            {
                GL.Uniform1(uniforms.ObjectId, request.Node.Id);
            }

            if (uniforms.MeshId != -1)
            {
                GL.Uniform1(uniforms.MeshId, (uint)request.Mesh.MeshIndex);
            }

            if (uniforms.ShaderId != -1)
            {
                GL.Uniform1(uniforms.ShaderId, (uint)request.Call.Material.Shader.NameHash);
            }

            if (uniforms.ShaderProgramId != -1)
            {
                GL.Uniform1(uniforms.ShaderProgramId, (uint)request.Call.Material.Shader.Program);
            }
            #endregion

            if (uniforms.CubeMapArrayIndices != -1 && request.Node.EnvMapIds != null)
            {
                GL.Uniform1(uniforms.CubeMapArrayIndices, request.Node.EnvMapIds.Length, request.Node.EnvMapIds);
            }

            if (uniforms.Animated != -1)
            {
                GL.Uniform1(uniforms.Animated, request.Mesh.AnimationTexture.HasValue ? 1.0f : 0.0f);
            }

            //Push animation texture to the shader (if it supports it)
            if (request.Mesh.AnimationTexture.HasValue)
            {
                if (uniforms.AnimationTexture != -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + (int)ReservedTextureSlots.AnimationTexture);
                    GL.BindTexture(TextureTarget.Texture2D, request.Mesh.AnimationTexture.Value);
                    GL.Uniform1(uniforms.AnimationTexture, (int)ReservedTextureSlots.AnimationTexture);
                }

                if (uniforms.NumBones != -1)
                {
                    var numBones = MathF.Max(1, request.Mesh.AnimationTextureSize - 1);
                    GL.Uniform1(uniforms.NumBones, numBones);
                }
            }

            var morphComposite = request.Mesh.FlexStateManager?.MorphComposite;
            if (morphComposite != null && uniforms.MorphCompositeTexture != -1)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + (int)ReservedTextureSlots.MorphCompositeTexture);
                GL.BindTexture(TextureTarget.Texture2D, morphComposite.CompositeTexture);
                GL.Uniform1(uniforms.MorphCompositeTexture, (int)ReservedTextureSlots.MorphCompositeTexture);

                if (uniforms.MorphCompositeTextureSize != -1)
                {
                    GL.Uniform2(uniforms.MorphCompositeTextureSize, (float)morphComposite.Width, (float)morphComposite.Height);
                }

                if (uniforms.MorphVertexIdOffset != -1)
                {
                    GL.Uniform1(uniforms.MorphVertexIdOffset, request.Call.VertexIdOffset);
                }
            }

            if (uniforms.Tint > -1)
            {
                var tint = (request.Mesh.Tint * request.Call.TintColor).ToOpenTK();
                GL.Uniform4(uniforms.Tint, tint);
            }

            GL.DrawElementsBaseVertex(
                request.Call.PrimitiveType,
                request.Call.IndexCount,
                request.Call.IndexType,
                request.Call.StartIndex,
                request.Call.BaseVertex
            );
        }
    }
}
