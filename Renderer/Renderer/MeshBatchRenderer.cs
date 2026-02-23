using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    static class MeshBatchRenderer
    {
        /// <summary>
        /// Draw call request with distance and render order for sorting.
        /// </summary>
        [DebuggerDisplay("{Node.DebugName,nq}")]
        public struct Request
        {
            public RenderableMesh Mesh;
            public DrawCall? Call;
            public float DistanceFromCamera;
            public int RenderOrder;
            public SceneNode Node;
        }

        record struct BatchRequest(RenderableMesh Mesh, DrawCall Call, SceneNode Node);

        public static int CompareCustomPipeline(Request a, Request b)
        {
            const int CustomRenderSortId = 500 * -RenderMaterial.PerShaderSortIdRange;

            return (a.Call, b.Call) switch
            {
                ({ }, { }) => b.Call.Material.SortId - a.Call.Material.SortId,
                (null, { }) => b.Call.Material.SortId - CustomRenderSortId,
                ({ }, null) => CustomRenderSortId - a.Call.Material.SortId,
                (null, null) => 0,
            };
        }

        public static int CompareRenderOrderThenPipeline(Request a, Request b)
        {
            if (a.RenderOrder == b.RenderOrder)
            {
                return a.Call!.Material.SortId - b.Call!.Material.SortId;
            }

            return a.RenderOrder - b.RenderOrder;
        }

        public static int CompareCameraDistance(Request a, Request b)
        {
            return -a.DistanceFromCamera.CompareTo(b.DistanceFromCamera);
        }

        public static int CompareAlphaTestThenProgram(Request a, Request b)
        {
            Debug.Assert(a.Call != null && b.Call != null);
            var alphaTestA = a.Call.Material.IsAlphaTest;
            var alphaTestB = b.Call.Material.IsAlphaTest;

            if (alphaTestA == alphaTestB)
            {
                return a.Call.Material.SortId - b.Call.Material.SortId;
            }

            return alphaTestA.CompareTo(alphaTestB);
        }

        public static bool IsAggregateWithNoVisibleChildren(Request req)
        {
            return req.Node is SceneAggregate { AnyChildrenVisible: false };
        }

        public static void Render(List<Request> requests, Scene.RenderContext context)
        {
            if (context.RenderPass == RenderPass.Opaque)
            {
                requests.Sort(CompareCustomPipeline);
            }
            else if (context.RenderPass == RenderPass.OpaqueAggregate)
            {
                using var _ = new GLDebugGroup("Sort Indirect Draw Dispatches");
                var removed = requests.RemoveAll(IsAggregateWithNoVisibleChildren);
                requests.Sort(CompareAlphaTestThenProgram);
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
            public int AnimationData = -1;
            public int EnvmapTexture = -1;
            public int LPVIrradianceTexture = -1;
            public int LPVIndicesTexture = -1;
            public int LPVScalarsTexture = -1;
            public int LPVShadowsTexture = -1;
            public int Transform = -1;
            public int IsInstancing = -1;
            public int Tint = -1;
            public int MeshId = -1;
            public int ShaderId = -1;
            public int ShaderProgramId = -1;
            public int MorphCompositeTexture = -1;
            public int MorphCompositeTextureSize = -1;
            public int MorphVertexIdOffset = -1;

            public Uniforms() { }
        }

        private ref struct Config
        {
            public bool NeedsCubemapBinding;
            public int LightmapGameVersionNumber;
            public bool IndirectDraw;
            public LightProbeType LightProbeType;
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
            Shader? shader = null;
            RenderMaterial? material = null;
            Uniforms uniforms = new();
            Config config = new()
            {
                NeedsCubemapBinding = context.Scene.LightingInfo.CubemapType == CubemapType.IndividualCubemaps,
                LightmapGameVersionNumber = context.Scene.LightingInfo.LightmapGameVersionNumber,
                LightProbeType = context.Scene.LightingInfo.LightProbeType,
                IndirectDraw = context.Scene.EnableIndirectDraws /* && context.RenderPass < RenderPass.Opaque*/,
            };

            foreach (var request in requests)
            {
                if (request.Call == null)
                {
                    if (context.RenderPass is RenderPass.Opaque or RenderPass.Translucent or RenderPass.Outline)
                    {
                        material?.PostRender();
                        request.Node.Render(context);
                        shader = null;
                        material = null;
                        vao = -1;
                    }

                    continue;
                }

                var requestMaterial = request.Call.Material;

                if (material != requestMaterial)
                {
                    if (context.ReplacementShader?.IgnoreMaterialData != true)
                    {
                        material?.PostRender();
                    }

                    var requestShader = context.ReplacementShader ?? requestMaterial.Shader;

                    // If the material did not change, shader could not have changed
                    if (shader != requestShader)
                    {
                        shader = requestShader;
                        uniforms = new Uniforms
                        {
                            AnimationData = shader.GetUniformLocation("uAnimationData"),
                            Transform = shader.GetUniformLocation("transform"),
                            IsInstancing = shader.GetUniformLocation("bIsInstancing"),
                            Tint = shader.GetUniformLocation("vTint"),
                        };

                        if (shader.Parameters.ContainsKey("S_SCENE_CUBEMAP_TYPE"))
                        {
                            uniforms.EnvmapTexture = shader.GetUniformLocation("g_tEnvironmentMap");
                        }

                        if (shader.Parameters.ContainsKey("F_MORPH_SUPPORTED"))
                        {
                            uniforms.MorphCompositeTexture = shader.GetUniformLocation("morphCompositeTexture");
                            uniforms.MorphCompositeTextureSize = shader.GetUniformLocation("morphCompositeTextureSize");
                            uniforms.MorphVertexIdOffset = shader.GetUniformLocation("morphVertexIdOffset");
                        }

                        if (shader.Parameters.ContainsKey("D_BAKED_LIGHTING_FROM_PROBE"))
                        {
                            uniforms.LPVIrradianceTexture = shader.GetUniformLocation("g_tLPV_Irradiance");
                            uniforms.LPVIndicesTexture = shader.GetUniformLocation("g_tLPV_Indices");
                            uniforms.LPVScalarsTexture = shader.GetUniformLocation("g_tLPV_Scalars");
                            uniforms.LPVShadowsTexture = shader.GetUniformLocation("g_tLPV_Shadows");
                        }

                        if (shader.Name == "vrf.picking")
                        {
                            uniforms.MeshId = shader.GetUniformLocation("meshId");
                            uniforms.ShaderId = shader.GetUniformLocation("shaderId");
                            uniforms.ShaderProgramId = shader.GetUniformLocation("shaderProgramId");
                        }

                        shader.Use();

                        if (!shader.IgnoreMaterialData)
                        {
                            foreach (var (slot, name, texture) in context.Textures)
                            {
                                shader.SetTexture((int)slot, name, texture);
                            }

                            context.Scene.LightingInfo.SetLightmapTextures(shader);
                        }

                        Debug.Assert(context.Scene.InstanceBufferGpu != null && context.Scene.TransformBufferGpu != null);
                        context.Scene.TransformBufferGpu.BindBufferBase();
                        context.Scene.InstanceBufferGpu.BindBufferBase();

                        if (config.IndirectDraw)
                        {
                            GL.ProgramUniform1((uint)shader.Program, uniforms.IsInstancing, 1);
                        }
                    }

                    material = requestMaterial;
                    material.Render(shader);
                }

                if (request.Call.VertexArrayObject == -1)
                {
                    request.Call.Material.Shader.EnsureLoaded();
                    request.Call.UpdateVertexArrayObject();
                }

                if (vao != request.Call.VertexArrayObject)
                {
                    vao = request.Call.VertexArrayObject;
                    GL.BindVertexArray(vao);
                }

                Draw(shader!, ref uniforms, ref config, new(request.Mesh, request.Call, request.Node));
            }

            if (vao > -1)
            {
                material!.PostRender();
                GL.BindVertexArray(0);
                GL.UseProgram(0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Draw(Shader shader, ref Uniforms uniforms, ref Config config, BatchRequest request)
        {
            if (uniforms.MeshId != -1)
            {
                GL.ProgramUniform1((uint)shader.Program, uniforms.MeshId, (uint)request.Mesh.MeshIndex);
                GL.ProgramUniform1((uint)shader.Program, uniforms.ShaderId, request.Call.Material.Shader.NameHash);
                GL.ProgramUniform1((uint)shader.Program, uniforms.ShaderProgramId, (uint)request.Call.Material.Shader.Program);
            }

            if (config.IndirectDraw)
            {
                if (request.Node is SceneAggregate agg && agg.IndirectDrawCount > 0 && agg.CompactionIndex >= 0)
                {
                    var scene = agg.Scene;
                    if (scene.EnableCompaction)
                    {
                        GL.MultiDrawElementsIndirectCount(
                            request.Call.PrimitiveType,
                            request.Call.IndexType,
                            agg.IndirectDrawByteOffset,
                            agg.CompactionIndex * sizeof(uint), // drawcount buffer offset
                            agg.IndirectDrawCount, // maxdrawcount
                            0); // stride
                        return;
                    }

                    GL.MultiDrawElementsIndirect(request.Call.PrimitiveType, request.Call.IndexType, agg.IndirectDrawByteOffset, agg.IndirectDrawCount, 0);
                    return;
                }
            }

            if (uniforms.EnvmapTexture != -1)
            {
                var isSteamVrEnvMaps = config.NeedsCubemapBinding && request.Node.EnvMaps.Count > 0;

                if (isSteamVrEnvMaps)
                {
                    var envmap = request.Node.EnvMaps[0];
                    SetInstanceTexture(shader, ReservedTextureSlots.EnvironmentMap, uniforms.EnvmapTexture, envmap.EnvMapTexture);
                }
            }

            if (uniforms.LPVIrradianceTexture != -1 && request.Node.LightProbeBinding is { } lightProbe)
            {
                if (config.LightProbeType == LightProbeType.IndividualProbes)
                {
                    if (lightProbe.Irradiance != null)
                    {
                        SetInstanceTexture(shader, ReservedTextureSlots.Probe1, uniforms.LPVIrradianceTexture, lightProbe.Irradiance);
                    }

                    if (config.LightmapGameVersionNumber == 1)
                    {
                        if (lightProbe.DirectLightIndices != null)
                        {
                            SetInstanceTexture(shader, ReservedTextureSlots.Probe2, uniforms.LPVIndicesTexture, lightProbe.DirectLightIndices);
                        }
                        if (lightProbe.DirectLightScalars != null)
                        {
                            SetInstanceTexture(shader, ReservedTextureSlots.Probe3, uniforms.LPVScalarsTexture, lightProbe.DirectLightScalars);
                        }
                    }
                    else if (request.Node.Scene.LightingInfo.LightmapGameVersionNumber == 2)
                    {
                        if (lightProbe.DirectLightShadows != null)
                        {
                            SetInstanceTexture(shader, ReservedTextureSlots.Probe2, uniforms.LPVShadowsTexture, lightProbe.DirectLightShadows);
                        }
                    }
                }
            }

            if (uniforms.AnimationData != -1)
            {
                var bAnimated = request.Mesh.BoneMatricesGpu != null;
                var numBones = 0u;
                var numWeights = 0u;
                var boneStart = 0u;

                if (bAnimated)
                {
                    request.Mesh.BoneMatricesGpu!.BindBufferBase();
                    numBones = (uint)request.Mesh.MeshBoneCount;
                    boneStart = (uint)request.Mesh.MeshBoneOffset;
                    numWeights = (uint)request.Mesh.BoneWeightCount;
                }

                GL.ProgramUniform4((uint)shader.Program, uniforms.AnimationData, bAnimated ? 1u : 0u, boneStart, numBones, numWeights);
            }

            if (uniforms.MorphVertexIdOffset != -1)
            {
                var morphComposite = request.Mesh.FlexStateManager?.MorphComposite;
                if (morphComposite != null)
                {
                    SetInstanceTexture(shader, ReservedTextureSlots.MorphCompositeTexture, uniforms.MorphCompositeTexture, morphComposite.CompositeTexture);
                    GL.ProgramUniform2(shader.Program, uniforms.MorphCompositeTextureSize, (float)morphComposite.CompositeTexture.Width, morphComposite.CompositeTexture.Height);
                }

                GL.ProgramUniform1(shader.Program, uniforms.MorphVertexIdOffset, morphComposite != null ? request.Call.VertexIdOffset : -1);
            }

            if (uniforms.Transform > -1)
            {
                var transform = request.Node.Transform.To3x4();
                GL.ProgramUniformMatrix3x4(shader.Program, uniforms.Transform, false, ref transform);
            }

            if (uniforms.Tint > -1)
            {
                var instanceTint = (request.Node is SceneAggregate.Fragment fragment) ? fragment.Tint : Vector4.One;
                var tint = request.Mesh.Tint * request.Call.TintColor * instanceTint;

                GL.ProgramUniform1((uint)shader.Program, uniforms.Tint, Color32.FromVector4(tint).PackedValue);
            }

            var instanceCount = 1;

            if (request.Node is SceneAggregate { InstanceTransformsGpu: not null } aggregate)
            {
                instanceCount = aggregate.InstanceTransforms.Count;
                aggregate.InstanceTransformsGpu.BindBufferBase();
            }

            if (uniforms.IsInstancing > -1)
            {
                GL.ProgramUniform1((uint)shader.Program, uniforms.IsInstancing, instanceCount > 1 ? 1 : 0);
            }

            GL.DrawElementsInstancedBaseVertexBaseInstance(
                request.Call.PrimitiveType,
                request.Call.IndexCount,
                request.Call.IndexType,
                request.Call.StartIndex,
                instanceCount,
                request.Call.BaseVertex,
                request.Node.Id
            );

            UnbindInstanceTextures();
        }
    }
}
