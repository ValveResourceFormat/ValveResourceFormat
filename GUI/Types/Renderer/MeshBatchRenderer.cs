using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class MeshBatchRenderer
    {
        public struct Request
        {
            public Matrix4x4 Transform;
            public RenderableMesh Mesh;
            public DrawCall Call;
            public float DistanceFromCamera;
        }

        public static void Render(List<Request> requests, Scene.RenderContext context)
        {
            // Opaque: Grouped by material
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Opaque)
            {
                foreach (var group in requests.GroupBy(r => r.Call.Material))
                {
                    DrawBatch(group.Key, group, context);
                }
            }

            // Translucent: In reverse order
            if (context.RenderPass == RenderPass.Both || context.RenderPass == RenderPass.Translucent)
            {
                var holder = new Request[1]; // Holds the one request we render at a time

                requests.Sort((a, b) => -a.DistanceFromCamera.CompareTo(b.DistanceFromCamera));

                foreach (var request in requests)
                {
                    holder[0] = request;
                    DrawBatch(request.Call.Material, holder, context);
                }
            }
        }

        private static void DrawBatch(RenderMaterial material, IEnumerable<Request> drawCalls, Scene.RenderContext context)
        {
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            var viewProjectionMatrix = context.Camera.ViewProjectionMatrix.ToOpenTK();
            var shader = drawCalls.First().Call.Shader;

            int uniformLocation;
            int uniformLocationAnimated = shader.GetUniformLocation("bAnimated");
            int uniformLocationAnimationTexture = shader.GetUniformLocation("animationTexture");
            int uniformLocationNumBones = shader.GetUniformLocation("fNumBones");
            int uniformLocationTransform = shader.GetUniformLocation("transform");
            int uniformLocationTint = shader.GetUniformLocation("m_vTintColorSceneObject");
            int uniformLocationTintDrawCall = shader.GetUniformLocation("m_vTintColorDrawCall");

            GL.UseProgram(shader.Program);

            uniformLocation = shader.GetUniformLocation("vLightPosition");
            GL.Uniform3(uniformLocation, context.Camera.Location.ToOpenTK());

            uniformLocation = shader.GetUniformLocation("vEyePosition");
            GL.Uniform3(uniformLocation, context.Camera.Location.ToOpenTK());

            uniformLocation = shader.GetUniformLocation("uProjectionViewMatrix");
            GL.UniformMatrix4(uniformLocation, false, ref viewProjectionMatrix);

            material.Render(shader);

            foreach (var request in drawCalls)
            {
                var transformTk = request.Transform.ToOpenTK();
                GL.UniformMatrix4(uniformLocationTransform, false, ref transformTk);

                uniformLocation = shader.GetUniformLocation("g_flTime");
                if (uniformLocation != 1)
                {
                    GL.Uniform1(uniformLocation, request.Mesh.Time);
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
                        var v = (float)Math.Max(1, request.Mesh.BoneCount - 1);
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

            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
        }
    }
}
