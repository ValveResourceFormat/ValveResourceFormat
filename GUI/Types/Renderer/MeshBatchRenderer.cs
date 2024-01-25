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
            public int EnvmapTexture;
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

        private ref struct Config
        {
            public bool NeedsCubemapBinding;
        }

        private static readonly Queue<int> instanceBoundTextures = new(capacity: 4);

        private static void DrawBatch(List<Request> requests, Scene.RenderContext context)
        {
            uint vao = 0;
            Shader shader = null;
            RenderMaterial material = null;
            Uniforms uniforms = new();
            Config config = new()
            {
                NeedsCubemapBinding = context.Scene.LightingInfo.CubemapType == Scene.CubemapType.IndividualCubemaps
            };

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
                            EnvmapTexture = shader.GetUniformLocation("g_tEnvironmentMap"),
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

                        foreach (var (slot, name, texture) in context.View.Textures)
                        {
                            shader.SetTexture((int)slot, name, texture);
                        }

                        context.Scene.LightingInfo.SetLightmapTextures(shader);
                        context.Scene.FogInfo.SetCubemapFogTexture(shader);
                    }

                    material = request.Call.Material;
                    material.Render(shader);
                }

                Draw(ref uniforms, ref config, request);
            }

            material?.PostRender();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(ref Uniforms uniforms, ref Config config, Request request)
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
                if (config.NeedsCubemapBinding && request.Node.EnvMaps.Count > 0)
                {
                    var envmap = request.Node.EnvMaps[0].EnvMapTexture;
                    var envmapDataIndex = request.Node.EnvMapIds[0];

                    instanceBoundTextures.Enqueue((int)ReservedTextureSlots.EnvironmentMap);
                    Shader.SetTexture((int)ReservedTextureSlots.EnvironmentMap, uniforms.EnvmapTexture, envmap);

                    GL.Uniform1(uniforms.CubeMapArrayIndices, envmapDataIndex);
                }
                else
                {
                    GL.Uniform1(uniforms.CubeMapArrayIndices, request.Node.EnvMapIds.Length, request.Node.EnvMapIds);
                }
            }

            if (uniforms.Animated != -1)
            {
                var bAnimated = request.Mesh.AnimationTexture != null;
                GL.Uniform1(uniforms.Animated, bAnimated ? 1u : 0u);

                if (bAnimated && uniforms.AnimationTexture != -1)
                {
                    instanceBoundTextures.Enqueue((int)ReservedTextureSlots.AnimationTexture);
                    Shader.SetTexture((int)ReservedTextureSlots.AnimationTexture, uniforms.AnimationTexture, request.Mesh.AnimationTexture);
                }
            }

            var morphComposite = request.Mesh.FlexStateManager?.MorphComposite;
            if (morphComposite != null && uniforms.MorphCompositeTexture != -1)
            {
                instanceBoundTextures.Enqueue((int)ReservedTextureSlots.MorphCompositeTexture);
                Shader.SetTexture((int)ReservedTextureSlots.MorphCompositeTexture, uniforms.MorphCompositeTexture, morphComposite.CompositeTexture);

                GL.Uniform2(uniforms.MorphCompositeTextureSize, (float)morphComposite.CompositeTexture.Width, (float)morphComposite.CompositeTexture.Height);
                GL.Uniform1(uniforms.MorphVertexIdOffset, request.Call.VertexIdOffset);
            }

            if (uniforms.Tint > -1)
            {
                var instanceTint = (request.Node is SceneAggregate.Fragment fragment) ? fragment.Tint : Vector4.One;
                var tint = (request.Mesh.Tint * request.Call.TintColor * instanceTint).ToOpenTK();

                GL.Uniform4(uniforms.Tint, tint);
            }

            GL.DrawElementsBaseVertex(
                request.Call.PrimitiveType,
                request.Call.IndexCount,
                request.Call.IndexType,
                request.Call.StartIndex,
                request.Call.BaseVertex
            );

            while (instanceBoundTextures.TryDequeue(out var slot))
            {
                GL.BindTextureUnit(slot, 0);
            }
        }
    }
}
