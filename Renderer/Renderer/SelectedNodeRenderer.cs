using System.Globalization;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Renders selection outlines and debug information for selected scene nodes.
    /// </summary>
    public class SelectedNodeRenderer
    {
        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool disableDepth;
        private bool debugCubeMaps;
        private bool debugLightProbes;
        private readonly List<SceneNode> selectedNodes = new(1);
        private readonly List<SimpleVertex> vertices = new(48);

        private readonly Vector2 SelectedNodeNameOffset = new(0, -20);
        public string ScreenDebugText { get; set; } = string.Empty;

        public SelectedNodeRenderer(RendererContext rendererContext)
        {
            shader = rendererContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

#if DEBUG
            var vaoLabel = nameof(SelectedNodeRenderer);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public void ToggleNode(SceneNode node)
        {
            var selectedNode = selectedNodes.IndexOf(node);

            if (selectedNode >= 0)
            {
                selectedNodes.RemoveAt(selectedNode);
                node.IsSelected = false;

                if (node.LightProbeBinding is { } probe)
                {
                    var probeStillInUse = selectedNodes.Any(n => n.LightProbeBinding == probe);

                    if (!probeStillInUse)
                    {
                        probe.RemoveDebugGridSpheres();
                    }
                }
            }
            else
            {
                selectedNodes.Add(node);
                node.IsSelected = true;
            }
        }

        public void SelectNode(SceneNode? node, bool forceDisableDepth = false)
        {
            RemoveAllLightProbeDebugGrid();

            selectedNodes.ForEach(static n => n.IsSelected = false);
            selectedNodes.Clear();

            if (node == null)
            {
                vertexCount = 0;
                return;
            }

            selectedNodes.Add(node);
            node.IsSelected = true;

            if (forceDisableDepth)
            {
                disableDepth = true;
            }
        }

        public void DisableSelectedNodes()
        {
            foreach (var node in selectedNodes)
            {
                node.LayerEnabled = !node.LayerEnabled;
            }
        }

        private static int ClosestVertexInView(Camera camera, ReadOnlySpan<Vector3> vertices)
        {
            var minDistance = float.MaxValue;
            var closestIndex = -1;

            for (var i = 0; i < vertices.Length; i++)
            {
                if (camera.ViewFrustum.Intersects(vertices[i]))
                {
                    var distance = Vector3.DistanceSquared(vertices[i], camera.Location);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestIndex = i;
                    }
                }
            }

            return closestIndex;
        }

        private static void AddBox(Camera camera, TextRenderer textRenderer, List<SimpleVertex> vertices, in Matrix4x4 transform, in AABB box, Color32 color, bool showSize = false)
        {
            // Adding a box will add many vertices, so ensure the required capacity for it up front
            vertices.EnsureCapacity(vertices.Count + 2 * 12);

            ReadOnlySpan<Vector3> c =
            [
                Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Max.Z), transform),
            ];

            ReadOnlySpan<(int Start, int End)> Lines =
            [
                (0, 1), (1, 2), (2, 3), (3, 0), // Bottom face
                (4, 5), (5, 6), (6, 7), (7, 4), // Top face
                (0, 4), (1, 5), (2, 6), (3, 7), // Vertical edges
            ];

            var closestIndex = showSize ? ClosestVertexInView(camera, c) : -1;

            for (var i = 0; i < Lines.Length; i++)
            {
                var line = Lines[i];

                if (closestIndex == line.Start || closestIndex == line.End)
                {
                    var axis = i >= 8 ? 2 : i % 2;

                    var axisColor = axis switch
                    {
                        0 => new Color32(1.0f, 0.2f, 0.2f, 1),
                        1 => new Color32(0.2f, 0.8f, 0.2f, 1),
                        2 => new Color32(0.2f, 0.2f, 1.0f, 1),
                        _ => color,
                    };

                    var (v0, v1) = (c[line.Start], c[line.End]);
                    var length = Vector3.Distance(v0, v1);

                    textRenderer.AddTextBillboard(Vector3.Lerp(v0, v1, 0.5f), new TextRenderer.TextRenderRequest
                    {
                        Scale = 13f,
                        Color = axisColor,
                        Text = length.ToString("0.##", CultureInfo.InvariantCulture),
                        CenterVertical = true,
                        CenterHorizontal = true,
                    }, camera);

                    ShapeSceneNode.AddLine(vertices, c[line.Start], c[line.End], axisColor);
                    continue;
                }

                ShapeSceneNode.AddLine(vertices, c[line.Start], c[line.End], color);
            }
        }

        public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
        {
            disableDepth = selectedNodes.Count > 1;

            if (selectedNodes.Count == 0)
            {
                // We don't need to reupload an empty array
                vertexCount = 0;
                return;
            }

            foreach (var node in selectedNodes)
            {
                var nodeName = node.Name ?? node.GetType().Name;

                if (node is not SimpleBoxSceneNode and not SpriteSceneNode)
                {
                    AddBox(renderContext.Camera, updateContext.TextRenderer, vertices, node.Transform, node.LocalBoundingBox, Color32.White, showSize: true);
                }

                if (debugCubeMaps)
                {
                    var tiedEnvmaps = renderContext.Scene.LightingInfo.CubemapType switch
                    {
                        CubemapType.CubemapArray => node.ShaderEnvMapVisibility
                            .GetVisibleShaderIndices()
                            .Select(shaderId => renderContext.Scene.LightingInfo.EnvMaps.FirstOrDefault(env => env.ShaderIndex == shaderId))
                            .OfType<SceneEnvMap>(),
                        _ => node.EnvMaps
                    };

                    var i = 0;

                    foreach (var tiedEnvMap in tiedEnvmaps)
                    {
                        AddBox(renderContext.Camera, updateContext.TextRenderer, vertices, tiedEnvMap.Transform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

                        if (renderContext.Scene.LightingInfo.CubemapType is CubemapType.IndividualCubemaps && i == 0)
                        {
                            ShapeSceneNode.AddLine(vertices, tiedEnvMap.Transform.Translation, node.BoundingBox.Center, new(0.0f, 1.0f, 0.0f, 1.0f));
                            i++;
                            continue;
                        }

                        var fractionToTen = Math.Min((float)i / 10, 1.0f);
                        var color = new Color32(1.0f, fractionToTen, fractionToTen, 1.0f);
                        ShapeSceneNode.AddLine(vertices, tiedEnvMap.Transform.Translation, node.BoundingBox.Center, color);
                        i++;
                    }
                }

                if (debugLightProbes && node.LightProbeBinding is not null)
                {
                    AddBox(renderContext.Camera, updateContext.TextRenderer, vertices, node.LightProbeBinding.Transform, node.LightProbeBinding.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
                    ShapeSceneNode.AddLine(vertices, node.LightProbeBinding.Transform.Translation, node.BoundingBox.Center, new(1.0f, 0.0f, 1.0f, 1.0f));

                    node.LightProbeBinding.CrateDebugGridSpheres();
                }

                if (node.EntityData != null)
                {
                    var classname = node.EntityData.GetProperty<string>("classname");
                    if (classname != null)
                    {
                        nodeName = classname;
                    }

                    if (classname is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_volumetric_fog_volume" or "env_wind_volume" or "steampal_kill_volume" or "env_cubemap_box" or "env_cubemap")
                    {
                        AABB bounds = default;

                        if (classname == "env_cubemap")
                        {
                            var radius = node.EntityData.GetProperty<float>("influenceradius");
                            bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
                        }
                        else
                        {
                            bounds = new AABB(
                                node.EntityData.GetVector3Property("box_mins"),
                                node.EntityData.GetVector3Property("box_maxs")
                            );
                        }

                        AddBox(renderContext.Camera, updateContext.TextRenderer, vertices, node.Transform, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

                        disableDepth = true;
                    }
                    else if (classname is "light_barn" or "light_omni2")
                    {
                        var boundsMins = node.EntityData.GetProperty<string>("precomputedboundsmins");
                        var boundsMaxs = node.EntityData.GetProperty<string>("precomputedboundsmaxs");
                        var obbExtent = node.EntityData.GetProperty<string>("precomputedobbextent");
                        var obbOrigin = node.EntityData.GetProperty<string>("precomputedobborigin");

                        if (boundsMins != null && boundsMaxs != null && obbExtent != null && obbOrigin != null)
                        {
                            var bounds = new AABB(
                                EntityTransformHelper.ParseVector(boundsMins),
                                EntityTransformHelper.ParseVector(boundsMaxs)
                            );

                            var origin = EntityTransformHelper.ParseVector(obbExtent);
                            var extent = EntityTransformHelper.ParseVector(obbOrigin);

                            AddBox(renderContext.Camera, updateContext.TextRenderer, vertices, Matrix4x4.Identity, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

                            ShapeSceneNode.AddLine(vertices, node.Transform.Translation, origin, new(0.0f, 0.0f, 1.0f, 1.0f));
                            ShapeSceneNode.AddLine(vertices, node.Transform.Translation, extent, new(1.0f, 1.0f, 0.0f, 1.0f));
                        }

                        disableDepth = true;
                    }
                }

                // draw node name above the bounding box
                var position = node.BoundingBox.Center;
                position.Z = node.BoundingBox.Max.Z;

                updateContext.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
                {
                    Scale = 20f,
                    Text = nodeName,
                    CenterVertical = true,
                    TextOffset = SelectedNodeNameOffset
                }, renderContext.Camera, fixedScale: false);
            }

            if (ScreenDebugText.Length > 0)
            {
                updateContext.TextRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
                {
                    X = 0.005f,
                    Y = 0.03f,
                    Scale = 14f,
                    Text = ScreenDebugText,
                }, renderContext.Camera);
            }

            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertexCount * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.DynamicDraw);

            vertices.Clear();
        }

        private void RemoveAllLightProbeDebugGrid()
        {
            foreach (var node in selectedNodes)
            {
                node.LightProbeBinding?.RemoveDebugGridSpheres();
            }
        }

        public void Render()
        {
            if (vertexCount == 0)
            {
                return;
            }

            GL.Enable(EnableCap.Blend);

            if (disableDepth)
            {
                GL.Disable(EnableCap.DepthTest);
            }

            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetUniform3x4("transform", Matrix4x4.Identity);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);
        }

        public void SetRenderMode(string mode)
        {
            debugCubeMaps = mode == "Cubemaps";
            debugLightProbes = mode is "Irradiance" or "Illumination";

            if (!debugLightProbes)
            {
                RemoveAllLightProbeDebugGrid();
            }
        }
    }
}
