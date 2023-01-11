using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal static class MeshBatchRenderer
    {
        public struct Request
        {
            public Matrix4x4 Transform;
            public RenderableMesh Mesh;
            public DrawCall Call;
            public float DistanceFromCamera;
            public int NodeId;
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

        private static void DrawBatch(IEnumerable<Request> drawCalls, Scene.RenderContext context)
        {
            GL.Enable(EnableCap.DepthTest);

            var viewProjectionMatrix = context.Camera.ViewProjectionMatrix.ToOpenTK();
            var cameraPosition = context.Camera.Location.ToOpenTK();
            var lightPosition = cameraPosition; // (context.LightPosition ?? context.Camera.Location).ToOpenTK();

            var groupedDrawCalls = context.ReplacementShader switch
            {
                null => drawCalls.GroupBy(a => a.Call.Shader),
                _ => drawCalls.GroupBy(a => context.ReplacementShader)
            };

            foreach (var shaderGroup in groupedDrawCalls)
            {
                var shader = shaderGroup.Key;

                var uniformLocationAnimated = shader.GetUniformLocation("bAnimated");
                var uniformLocationAnimationTexture = shader.GetUniformLocation("animationTexture");
                var uniformLocationNumBones = shader.GetUniformLocation("fNumBones");
                var uniformLocationTransform = shader.GetUniformLocation("transform");
                var uniformLocationTint = shader.GetUniformLocation("m_vTintColorSceneObject");
                var uniformLocationTintDrawCall = shader.GetUniformLocation("m_vTintColorDrawCall");
                var uniformLocationTime = shader.GetUniformLocation("g_flTime");
                var uniformLocationScId = shader.GetUniformLocation("sceneObjectId");

                GL.UseProgram(shader.Program);

                GL.Uniform3(shader.GetUniformLocation("vLightPosition"), lightPosition);
                GL.Uniform3(shader.GetUniformLocation("vEyePosition"), cameraPosition);
                GL.UniformMatrix4(shader.GetUniformLocation("uProjectionViewMatrix"), false, ref viewProjectionMatrix);

                foreach (var materialGroup in shaderGroup.GroupBy(a => a.Call.Material))
                {
                    var material = materialGroup.Key;

                    if (!context.RenderToolsMaterials && material.IsToolsMaterial)
                    {
                        continue;
                    }

                    material.Render(shader);

                    foreach (var request in materialGroup)
                    {
                        var transformTk = request.Transform.ToOpenTK();
                        GL.UniformMatrix4(uniformLocationTransform, false, ref transformTk);

                        if (uniformLocationScId != 1)
                        {
                            GL.Uniform1(uniformLocationScId, (uint)request.NodeId);
                        }

                        if (uniformLocationTime != 1)
                        {
                            GL.Uniform1(uniformLocationTime, request.Mesh.Time);
                        }

                        if (uniformLocationAnimated != -1)
                        {
                            GL.Uniform1(uniformLocationAnimated, request.Mesh.AnimationTexture.HasValue ? 1.0f : 0.0f);
                        }

                        //Push animation texture to the shader (if it supports it)
                        if (request.Mesh.AnimationTexture.HasValue)
                        {
                            if (uniformLocationAnimationTexture != -1)
                            {
                                GL.ActiveTexture(TextureUnit.Texture0);
                                GL.BindTexture(TextureTarget.Texture2D, request.Mesh.AnimationTexture.Value);
                                GL.Uniform1(uniformLocationAnimationTexture, 0);
                            }

                            if (uniformLocationNumBones != -1)
                            {
                                var v = (float)Math.Max(1, request.Mesh.AnimationTextureSize - 1);
                                GL.Uniform1(uniformLocationNumBones, v);
                            }
                        }

                        if (uniformLocationTint > -1)
                        {
                            var tint = request.Mesh.Tint.ToOpenTK();
                            GL.Uniform4(uniformLocationTint, tint);
                        }

                        if (uniformLocationTintDrawCall > -1)
                        {
                            GL.Uniform3(uniformLocationTintDrawCall, request.Call.TintColor);
                        }

                        GL.BindVertexArray(request.Call.VertexArrayObject);
                        GL.DrawElements(request.Call.PrimitiveType, request.Call.IndexCount, request.Call.IndexType, (IntPtr)request.Call.StartIndex);
                    }

                    material.PostRender();
                }
            }

            GL.Disable(EnableCap.DepthTest);
        }
    }
}
