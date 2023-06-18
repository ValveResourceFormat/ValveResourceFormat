using System;
using System.Collections.Generic;
using System.Linq;
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
            public int Time;
            public int ObjectId;
            public int MeshId;
            public int CubeMapArrayIndex;
        }

        /// <summary>
        /// Minimizes state changes by grouping draw calls by shader and material.
        /// </summary>
        private static void DrawBatch(IEnumerable<Request> drawCalls, Scene.RenderContext context)
        {
            GL.Enable(EnableCap.DepthTest);

            var viewProjectionMatrix = context.Camera.ViewProjectionMatrix.ToOpenTK();
            var cameraPosition = context.Camera.Location.ToOpenTK();
            var dirLight = (context.GlobalLightTransform ?? context.Camera.CameraViewMatrix).ToOpenTK();
            var dirLightColor = context.GlobalLightColor.ToOpenTK();

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
                    Time = shader.GetUniformLocation("g_flTime"),
                    ObjectId = shader.GetUniformLocation("sceneObjectId"),
                    CubeMapArrayIndex = shader.GetUniformLocation("g_iEnvironmentMapArrayIndex"),
                    MeshId = shader.GetUniformLocation("meshId"),
                };

                GL.UseProgram(shader.Program);

                GL.UniformMatrix4(shader.GetUniformLocation("vLightPosition"), false, ref dirLight);
                GL.Uniform4(shader.GetUniformLocation("vLightColor"), dirLightColor);
                GL.Uniform3(shader.GetUniformLocation("vEyePosition"), cameraPosition);
                GL.UniformMatrix4(shader.GetUniformLocation("uProjectionViewMatrix"), false, ref viewProjectionMatrix);

                if (context.LightingInfo?.EnvMapPositionsUniform is not null)
                {
                    var count = context.LightingInfo.EnvMaps.Count;
                    GL.Uniform4(shader.GetUniformLocation("g_vEnvMapPositionWs"), count, context.LightingInfo.EnvMapPositionsUniform);
                    GL.Uniform4(shader.GetUniformLocation("g_vEnvMapBoxMins"), count, context.LightingInfo.EnvMapMinsUniform);
                    GL.Uniform4(shader.GetUniformLocation("g_vEnvMapBoxMaxs"), count, context.LightingInfo.EnvMapMaxsUniform);
                }

                foreach (var materialGroup in shaderGroup.GroupBy(a => a.Call.Material))
                {
                    var material = materialGroup.Key;

                    if (!context.RenderToolsMaterials && material.IsToolsMaterial)
                    {
                        continue;
                    }

                    material.Render(shader, context.LightingInfo);

                    foreach (var request in materialGroup)
                    {
                        Draw(uniforms, request, context);
                    }

                    material.PostRender();
                }
            }

            GL.Disable(EnableCap.DepthTest);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(Uniforms uniforms, Request request, Scene.RenderContext context)
        {
            var transformTk = request.Transform.ToOpenTK();
            GL.UniformMatrix4(uniforms.Transform, false, ref transformTk);

            if (uniforms.ObjectId != -1)
            {
                GL.Uniform1(uniforms.ObjectId, request.Node.Id);
            }

            if (uniforms.CubeMapArrayIndex != -1)
            {
                var arrayIndex = 0;

                if (request.Node.CubeMapPrecomputedHandshake > 0 && context.LightingInfo.EnvMaps.TryGetValue(request.Node.CubeMapPrecomputedHandshake, out var envMap))
                {
                    arrayIndex = envMap.ArrayIndex;
                }

                GL.Uniform1(uniforms.CubeMapArrayIndex, arrayIndex);
            }

            if (uniforms.MeshId != -1)
            {
                GL.Uniform1(uniforms.MeshId, (uint)request.Mesh.MeshIndex);
            }

            if (uniforms.Time != 1)
            {
                GL.Uniform1(uniforms.Time, context.Time);
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
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, request.Mesh.AnimationTexture.Value);
                    GL.Uniform1(uniforms.AnimationTexture, 0);
                }

                if (uniforms.NumBones != -1)
                {
                    var v = (float)Math.Max(1, request.Mesh.AnimationTextureSize - 1);
                    GL.Uniform1(uniforms.NumBones, v);
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
