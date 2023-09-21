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
            public int OverlayRenderOrder;
            public SceneNode Node;
        }

        public static void Render(List<Request> requests, Scene.RenderContext context)
        {
            // Opaque: Grouped by material
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Opaque)
            {
                DrawBatch(requests, context, false);
            }

            // Translucent: In reverse order
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Translucent)
            {
                DrawBatch(requests, context, true);
            }
        }

        private ref struct Uniforms
        {
            public int Animated;
            public int AnimationTexture;
            public int NumBones;
            public int Transform;
            public int Tint;
            public int TintDrawCall;
            public int ObjectId;
            public int MeshId;
            public int ShaderId;
            public int ShaderProgramId;
            public int CubeMapArrayIndices;
        }

        /// <summary>
        /// Minimizes state changes by grouping draw calls by shader and material.
        /// </summary>
        private static void DrawBatch(List<Request> requests, Scene.RenderContext context, bool sortByDistance)
        {
            // Sort draw call requests by shader, and then by material
            if (sortByDistance)
            {
                requests.Sort(static (a, b) => -a.DistanceFromCamera.CompareTo(b.DistanceFromCamera));
            }
            else
            {
                requests.Sort(static (a, b) =>
                {
                    if (a.OverlayRenderOrder == b.OverlayRenderOrder)
                    {
                        if (a.Call.Material.Shader.Program == b.Call.Material.Shader.Program)
                        {
                            return a.Call.Material.GetHashCode() - b.Call.Material.GetHashCode();
                        }

                        return a.Call.Material.Shader.Program - b.Call.Material.Shader.Program;
                    }

                    return a.OverlayRenderOrder - b.OverlayRenderOrder;
                });
            }

            Shader shader = null;
            RenderMaterial material = null;
            Uniforms uniforms = new();

            foreach (var request in requests)
            {
                if (!context.Scene.ShowToolsMaterials && request.Call.Material.IsToolsMaterial)
                {
                    continue;
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
                            Tint = shader.GetUniformLocation("m_vTintColorSceneObject"),
                            TintDrawCall = shader.GetUniformLocation("m_vTintColorDrawCall"),
                            CubeMapArrayIndices = shader.GetUniformLocation("g_iEnvMapArrayIndices"),
                            ObjectId = shader.GetUniformLocation("sceneObjectId"),
                            MeshId = shader.GetUniformLocation("meshId"),
                            ShaderId = shader.GetUniformLocation("shaderId"),
                            ShaderProgramId = shader.GetUniformLocation("shaderProgramId")
                        };

                        GL.UseProgram(shader.Program);

                        foreach (var buffer in context.Buffers)
                        {
                            buffer.SetBlockBinding(shader);
                        }

                        context.FogInfo.SetCubemapFogTexture(shader);
                    }

                    material = request.Call.Material;
                    material.Render(shader, context.LightingInfo);
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

            if (uniforms.CubeMapArrayIndices != -1)
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

            if (uniforms.Tint > -1)
            {
                var tint = request.Mesh.Tint.ToOpenTK();
                GL.Uniform4(uniforms.Tint, tint);
            }

            if (uniforms.TintDrawCall > -1)
            {
                GL.Uniform3(uniforms.TintDrawCall, request.Call.TintColor);
            }

            GL.BindVertexArray(request.Call.VertexArrayObject);
            GL.DrawElements(request.Call.PrimitiveType, request.Call.IndexCount, request.Call.IndexType, (IntPtr)request.Call.StartIndex);
        }
    }
}
