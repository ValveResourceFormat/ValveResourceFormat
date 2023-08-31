using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
            public SceneNode Node;
        }

        public static void Render(List<Request> requests, Scene.RenderContext context)
        {
            // Opaque: Grouped by material
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Opaque)
            {
                DrawBatch(requests, context);
            }

            // Translucent: In reverse order
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Translucent)
            {
                var holder = new Request[1]; // Holds the one request we render at a time

                requests.Sort((a, b) => -a.DistanceFromCamera.CompareTo(b.DistanceFromCamera));

                foreach (var request in requests)
                {
                    holder[0] = request;
                    DrawBatch(holder, context);
                }
            }
        }

        private struct Uniforms
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
        private static void DrawBatch(IEnumerable<Request> drawCalls, Scene.RenderContext context)
        {
            GL.Enable(EnableCap.DepthTest);

            var groupedDrawCalls = context.ReplacementShader == null
                ? drawCalls.GroupBy(a => a.Call.Shader)
                : drawCalls.GroupBy(a => context.ReplacementShader);

            foreach (var shaderGroup in groupedDrawCalls)
            {
                var shader = shaderGroup.Key;
                var uniforms = new Uniforms
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

                foreach (var materialGroup in shaderGroup.GroupBy(a => a.Call.Material))
                {
                    var material = materialGroup.Key;

                    if (!context.Scene.ShowToolsMaterials && material.IsToolsMaterial)
                    {
                        continue;
                    }


                    material.Render(shader, context.LightingInfo);

                    foreach (var request in materialGroup)
                    {
                        Draw(uniforms, request);
                    }

                    material.PostRender();
                }
            }

            GL.Disable(EnableCap.DepthTest);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(Uniforms uniforms, Request request)
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
                GL.Uniform1(uniforms.ShaderId, (uint)request.Call.Shader.NameHash);
            }

            if (uniforms.ShaderProgramId != -1)
            {
                GL.Uniform1(uniforms.ShaderProgramId, (uint)request.Call.Shader.Program);
            }
            #endregion

            if (uniforms.CubeMapArrayIndices != -1)
            {
                var indices = request.Node.EnvMaps.Select(x => x.ArrayIndex).ToArray();
                GL.Uniform1(uniforms.CubeMapArrayIndices, indices.Length, indices);
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
