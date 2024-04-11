using System.Diagnostics;
using System.Runtime.CompilerServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;




namespace GUI.Types.Renderer
{
    static class MeshBatchRenderer
    {
        [DebuggerDisplay("{Node.DebugName,nq}")]
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
            public int Animated = -1;
            public int AnimationTexture = -1;
            public int EnvmapTexture = -1;
            public int LightProbeVolumeData = -1;
            public int LPVIrradianceTexture = -1;
            public int LPVIndicesTexture = -1;
            public int LPVScalarsTexture = -1;
            public int LPVShadowsTexture = -1;
            public int Transform = -1;
            public int Tint = -1;
            public int ObjectId = -1;
            public int MeshId = -1;
            public int ShaderId = -1;
            public int ShaderProgramId = -1;
            public int CubeMapArrayIndices = -1;
            public int MorphCompositeTexture = -1;
            public int MorphCompositeTextureSize = -1;
            public int MorphVertexIdOffset = -1;

            public Uniforms() { }
        }

        private ref struct Config
        {
            public bool NeedsCubemapBinding;
            public int LightmapGameVersionNumber;
            public Scene.LightProbeType LightProbeType;
        }

        private static readonly Queue<int> instanceBoundTextures = new(capacity: 4);

        private static void SetInstanceTexture(Shader shader, ReservedTextureSlots slot, int location, RenderTexture texture)
        {
            var slotIndex = (int)slot;
            instanceBoundTextures.Enqueue(slotIndex);
            shader.SetTexture(slotIndex, location, texture);
        }

        private static void UnbindInstanceTextures()
        {
            while (instanceBoundTextures.TryDequeue(out var slot))
            {
                GL.BindTextureUnit(slot, 0);
            }
        }

        private static void DrawBatch(List<Request> requests, Scene.RenderContext context)
        {
            var vao = -1;
            Shader shader = null;
            RenderMaterial material = null;
            Uniforms uniforms = new();
            Config config = new()
            {
                NeedsCubemapBinding = context.Scene.LightingInfo.CubemapType == Scene.CubemapType.IndividualCubemaps,
                LightmapGameVersionNumber = context.Scene.LightingInfo.LightmapGameVersionNumber,
                LightProbeType = context.Scene.LightingInfo.LightProbeType,
            };


            //TEMPORARY FRAMEBUFFER CREATION
            Framebuffer tempFramebuffer;
            tempFramebuffer = Framebuffer.Prepare(context.Framebuffer.Width, context.Framebuffer.Height, 0, context.Framebuffer.ColorFormat, context.Framebuffer.DepthFormat);
            tempFramebuffer.Initialize();
            tempFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            tempFramebuffer.ClearColor = new OpenTK.Graphics.Color4(255, 0, 255, 255);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, context.Framebuffer.FboHandle);


            foreach (var request in requests)
            {
                if (vao != request.Call.VertexArrayObject)
                {
                    vao = request.Call.VertexArrayObject;
                    GL.BindVertexArray(vao);
                }

                var requestMaterial = request.Call.Material;

                if (material != requestMaterial)
                {
                    material?.PostRender();

                    var requestShader = context.ReplacementShader ?? requestMaterial.Shader;

                    // If the material did not change, shader could not have changed
                    if (shader != requestShader)
                    {
                        shader = requestShader;
                        uniforms = new Uniforms
                        {
                            Animated = shader.GetUniformLocation("bAnimated"),
                            AnimationTexture = shader.GetUniformLocation("animationTexture"),
                            Transform = shader.GetUniformLocation("transform"),
                            Tint = shader.GetUniformLocation("vTint"),
                        };

                        if (shader.Parameters.ContainsKey("SCENE_CUBEMAP_TYPE"))
                        {
                            uniforms.EnvmapTexture = shader.GetUniformLocation("g_tEnvironmentMap");
                            uniforms.CubeMapArrayIndices = shader.GetUniformLocation("g_iEnvMapArrayIndices");
                        }

                        if (shader.Parameters.ContainsKey("F_MORPH_SUPPORTED"))
                        {
                            uniforms.MorphCompositeTexture = shader.GetUniformLocation("morphCompositeTexture");
                            uniforms.MorphCompositeTextureSize = shader.GetUniformLocation("morphCompositeTextureSize");
                            uniforms.MorphVertexIdOffset = shader.GetUniformLocation("morphVertexIdOffset");
                        }

                        if (shader.Parameters.ContainsKey("D_BAKED_LIGHTING_FROM_PROBE"))
                        {
                            uniforms.LightProbeVolumeData = shader.GetUniformBlockIndex("LightProbeVolume");
                            uniforms.LPVIrradianceTexture = shader.GetUniformLocation("g_tLPV_Irradiance");
                            uniforms.LPVIndicesTexture = shader.GetUniformLocation("g_tLPV_Indices");
                            uniforms.LPVScalarsTexture = shader.GetUniformLocation("g_tLPV_Scalars");
                            uniforms.LPVShadowsTexture = shader.GetUniformLocation("g_tLPV_Shadows");
                        }

                        if (shader.Name == "vrf.picking")
                        {
                            uniforms.ObjectId = shader.GetUniformLocation("sceneObjectId");
                            uniforms.MeshId = shader.GetUniformLocation("meshId");
                            uniforms.ShaderId = shader.GetUniformLocation("shaderId");
                            uniforms.ShaderProgramId = shader.GetUniformLocation("shaderProgramId");
                        }

                        GL.UseProgram(shader.Program);

                        foreach (var (slot, name, texture) in context.View.Textures)
                        {
                            shader.SetTexture((int)slot, name, texture);
                        }

                        context.Scene.LightingInfo.SetLightmapTextures(shader);
                        context.Scene.FogInfo.SetCubemapFogTexture(shader);
                    }

                    material = requestMaterial;
                    material.Render(shader);
                }

                if (shader.FileName == "water_csgo")
                {
                    var (w, h) = (context.Framebuffer.Width, context.Framebuffer.Height);
                    GL.BlitNamedFramebuffer(context.Framebuffer.FboHandle, tempFramebuffer.FboHandle, 0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                    GL.BindFramebuffer(FramebufferTarget.FramebufferExt, tempFramebuffer.FboHandle);

                    //tempFramebuffer.Clear(); //this is here because its easier to tell if something is screwed with read operations
                    tempFramebuffer.Clear();


                    //Verify that the buffer contains proper data with the following code:

                    //GL.BindTexture(TextureTarget.Texture2D, tempFramebuffer.Color.Handle);
                    //int[] pixelData = new int[tempFramebuffer.Width * tempFramebuffer.Height];
                    //GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgb, PixelType.UnsignedByte, pixelData);
                    //int red = System.Drawing.Color.FromArgb(pixelData[0]).R;
                    //int green = System.Drawing.Color.FromArgb(pixelData[0]).G;
                    //int blue = System.Drawing.Color.FromArgb(pixelData[0]).B;

                    //ErrorCode testerror = GL.GetError();

                    
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, context.Framebuffer.FboHandle);

                    shader.SetTexture(0, "depth_map", context.Framebuffer.Depth);
                    shader.SetTexture(1, "color_map", context.Framebuffer.Color);       //Note: I will probably not retain the MS framebuffer, very little point to doing this, probably invisible difference
                    shader.SetTexture(2, "color_map_reduced", tempFramebuffer.Color);

                    shader.SetUniform2("resolution", new Vector2(context.Framebuffer.Width, context.Framebuffer.Height));
                }

                Draw(shader, ref uniforms, ref config, request);
            }

            if (vao > -1)
            {
                material.PostRender();
                GL.BindVertexArray(0);
                GL.UseProgram(0);
            }
            //TEMPORARY FRAMEBUFFER DESTRUCTION
            tempFramebuffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(Shader shader, ref Uniforms uniforms, ref Config config, Request request)
        {
            var transformTk = request.Transform.ToOpenTK();
            GL.ProgramUniformMatrix4(shader.Program, uniforms.Transform, false, ref transformTk);

            if (uniforms.ObjectId != -1)
            {
                GL.ProgramUniform1((uint)shader.Program, uniforms.ObjectId, request.Node.Id);
                GL.ProgramUniform1((uint)shader.Program, uniforms.MeshId, (uint)request.Mesh.MeshIndex);
                GL.ProgramUniform1((uint)shader.Program, uniforms.ShaderId, (uint)request.Call.Material.Shader.NameHash);
                GL.ProgramUniform1((uint)shader.Program, uniforms.ShaderProgramId, (uint)request.Call.Material.Shader.Program);
            }

            if (uniforms.CubeMapArrayIndices != -1 && request.Node.EnvMapIds != null)
            {
                if (config.NeedsCubemapBinding && request.Node.EnvMaps.Count > 0)
                {
                    var envmap = request.Node.EnvMaps[0].EnvMapTexture;
                    var envmapDataIndex = request.Node.EnvMapIds[0];

                    SetInstanceTexture(shader, ReservedTextureSlots.EnvironmentMap, uniforms.EnvmapTexture, envmap);
                    GL.ProgramUniform1(shader.Program, uniforms.CubeMapArrayIndices, envmapDataIndex);
                }
                else
                {
                    GL.ProgramUniform1(shader.Program, uniforms.CubeMapArrayIndices, request.Node.EnvMapIds.Length, request.Node.EnvMapIds);
                }
            }

            if (uniforms.LightProbeVolumeData != -1 && request.Node.LightProbeBinding is { } lightProbe)
            {
                lightProbe.SetGpuProbeData(config.LightProbeType == Scene.LightProbeType.ProbeAtlas);

                if (config.LightProbeType == Scene.LightProbeType.IndividualProbes)
                {
                    SetInstanceTexture(shader, ReservedTextureSlots.Probe1, uniforms.LPVIrradianceTexture, lightProbe.Irradiance);

                    if (config.LightmapGameVersionNumber == 1)
                    {
                        SetInstanceTexture(shader, ReservedTextureSlots.Probe2, uniforms.LPVIndicesTexture, lightProbe.DirectLightIndices);
                        SetInstanceTexture(shader, ReservedTextureSlots.Probe3, uniforms.LPVScalarsTexture, lightProbe.DirectLightScalars);
                    }
                    else if (request.Node.Scene.LightingInfo.LightmapGameVersionNumber == 2)
                    {
                        SetInstanceTexture(shader, ReservedTextureSlots.Probe2, uniforms.LPVShadowsTexture, lightProbe.DirectLightShadows);
                    }
                }
            }

            if (uniforms.Animated != -1)
            {
                var bAnimated = request.Mesh.AnimationTexture != null;
                GL.ProgramUniform1((uint)shader.Program, uniforms.Animated, bAnimated ? 1u : 0u);

                if (bAnimated && uniforms.AnimationTexture != -1)
                {
                    SetInstanceTexture(shader, ReservedTextureSlots.AnimationTexture, uniforms.AnimationTexture, request.Mesh.AnimationTexture);
                }
            }

            if (uniforms.MorphVertexIdOffset != -1)
            {
                var morphComposite = request.Mesh.FlexStateManager?.MorphComposite;
                if (morphComposite != null)
                {
                    SetInstanceTexture(shader, ReservedTextureSlots.MorphCompositeTexture, uniforms.MorphCompositeTexture, morphComposite.CompositeTexture);
                    GL.ProgramUniform2(shader.Program, uniforms.MorphCompositeTextureSize, (float)morphComposite.CompositeTexture.Width, (float)morphComposite.CompositeTexture.Height);
                }

                GL.ProgramUniform1(shader.Program, uniforms.MorphVertexIdOffset, morphComposite != null ? request.Call.VertexIdOffset : -1);
            }

            if (uniforms.Tint > -1)
            {
                var instanceTint = (request.Node is SceneAggregate.Fragment fragment) ? fragment.Tint : Vector4.One;
                var tint = request.Mesh.Tint * request.Call.TintColor * instanceTint;

                GL.ProgramUniform4(shader.Program, uniforms.Tint, tint.X, tint.Y, tint.Z, tint.W);
            }

            GL.DrawElementsBaseVertex(
                request.Call.PrimitiveType,
                request.Call.IndexCount,
                request.Call.IndexType,
                request.Call.StartIndex,
                request.Call.BaseVertex
            );

            UnbindInstanceTextures();
        }
    }
}
