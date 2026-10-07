using System.Globalization;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Renders selection outlines and debug information for selected scene nodes, over the final image so their colors
    /// show as given: lines and faces solid where the scene does not hide them, and faint over it.
    /// </summary>
    public class SelectedNodeRenderer
    {
        private bool debugCubeMaps;
        private bool debugLightProbes;
        private readonly List<SceneNode> selectedNodes = new(1);
        private readonly HelperVertices vertices = new();

        private readonly LineBuffer lines;
        private readonly LineBuffer overlayLines;
        private readonly LineBuffer faces;
        private readonly LineBuffer overlayFaces;

        private readonly Vector2 SelectedNodeNameOffset = new(0, -20);

        // Opacity of the bounds over the scene, out of 255
        private const byte HiddenLineAlpha = 32;

        /// <summary>Gets or sets optional debug text rendered in the top-left corner of the viewport.</summary>
        public string ScreenDebugText { get; set; } = string.Empty;

        /// <summary>Gets a value indicating whether any node is currently selected.</summary>
        public bool HasSelectedNodes => selectedNodes.Count > 0;

        /// <summary>Initializes the selected node renderer and creates GPU resources.</summary>
        /// <param name="rendererContext">Renderer context for loading shaders.</param>
        public SelectedNodeRenderer(RendererContext rendererContext)
        {
            const string Shader = "helper_lines";

            lines = new LineBuffer(rendererContext, nameof(SelectedNodeRenderer), Shader);
            overlayLines = new LineBuffer(rendererContext, nameof(SelectedNodeRenderer) + " overlay", Shader);
            faces = new LineBuffer(rendererContext, nameof(SelectedNodeRenderer) + " faces", Shader);
            overlayFaces = new LineBuffer(rendererContext, nameof(SelectedNodeRenderer) + " overlay faces", Shader);
        }

        /// <summary>Toggles selection of the given node, adding it if not selected or removing it if already selected.</summary>
        /// <param name="node">The scene node to toggle.</param>
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

        /// <summary>Clears the selection and selects a single node.</summary>
        /// <param name="node">Node to select, or <see langword="null"/> to clear the selection.</param>
        public void SelectNode(SceneNode? node)
        {
            RemoveAllLightProbeDebugGrid();

            selectedNodes.ForEach(static n => n.IsSelected = false);
            selectedNodes.Clear();

            if (node == null)
            {
                ClearBuffers();
                return;
            }

            selectedNodes.Add(node);
            node.IsSelected = true;
        }

        /// <summary>Toggles the layer-enabled state of all currently selected nodes.</summary>
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

        // Solid where seen, and faint over the scene
        private void AddLine(Vector3 start, Vector3 end, Color32 color)
            => vertices.AddLine(start, end, color with { A = HiddenLineAlpha }, HelperPasses.Both);

        private void AddBox(Camera camera, TextRenderer textRenderer, in Matrix4x4 transform, in AABB box, Color32 color, bool showSize = false)
        {
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

                    AddLine(c[line.Start], c[line.End], axisColor);
                    continue;
                }

                AddLine(c[line.Start], c[line.End], color);
            }
        }

        /// <summary>Rebuilds the wireframe geometry and text labels for all selected nodes.</summary>
        /// <param name="renderContext">Render context providing camera and scene state.</param>
        /// <param name="updateContext">Update context providing the text renderer.</param>
        public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
        {
            // Draw the debug text even when nothing is selected
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

            if (selectedNodes.Count == 0)
            {
                // We don't need to reupload an empty array
                ClearBuffers();
                return;
            }

            foreach (var node in selectedNodes)
            {
                var nodeName = node.Name ?? node.GetType().Name;

                // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
                var toWorld = node.Scene.ToViewerWorld;
                var bounds = node.BoundingBox.Transform(toWorld);
                var hasHelpers = false;

                if (debugCubeMaps)
                {
                    IEnumerable<SceneEnvMap> tiedEnvmaps = node.EnvMaps;
                    if (renderContext.Scene.LightingInfo.CubemapType == CubemapType.CubemapArray)
                    {
                        var list = new List<SceneEnvMap>();
                        foreach (var shaderId in node.ShaderEnvMapVisibility.GetVisibleShaderIndices())
                        {
                            var env = renderContext.Scene.LightingInfo.EnvMaps.FirstOrDefault(e => e.ShaderIndex == shaderId);
                            if (env is SceneEnvMap sem)
                            {
                                list.Add(sem);
                            }
                        }
                        tiedEnvmaps = list;
                    }

                    var i = 0;

                    foreach (var tiedEnvMap in tiedEnvmaps)
                    {
                        var envMapTransform = tiedEnvMap.Transform * toWorld;

                        AddBox(renderContext.Camera, updateContext.TextRenderer, envMapTransform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

                        if (renderContext.Scene.LightingInfo.CubemapType is CubemapType.IndividualCubemaps && i == 0)
                        {
                            AddLine(envMapTransform.Translation, bounds.Center, new(0.0f, 1.0f, 0.0f, 1.0f));
                            i++;
                            continue;
                        }

                        var fractionToTen = Math.Min((float)i / 10, 1.0f);
                        var color = new Color32(1.0f, fractionToTen, fractionToTen, 1.0f);
                        AddLine(envMapTransform.Translation, bounds.Center, color);
                        i++;
                    }
                }

                if (debugLightProbes && node.LightProbeBinding is not null)
                {
                    var probeTransform = node.LightProbeBinding.Transform * toWorld;

                    AddBox(renderContext.Camera, updateContext.TextRenderer, probeTransform, node.LightProbeBinding.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
                    AddLine(probeTransform.Translation, bounds.Center, new(1.0f, 0.0f, 1.0f, 1.0f));

                    node.LightProbeBinding.CreateDebugGridSpheres();
                }

                if (node.EntityData != null)
                {
                    var classname = node.EntityData.GetStringProperty("classname");
                    if (classname != null)
                    {
                        nodeName = classname;
                    }

                    var placement = node.EntityInstance?.RigidTransform ?? (node is SpriteSceneNode or SimpleBoxSceneNode
                        ? EntityTransformHelper.ToRigidTransformationMatrix(node.EntityData)
                        : node.Transform);

                    hasHelpers = EntityHelperLines.TryAdd(vertices, classname, node.EntityData, node.EntityInstance, placement, toWorld);
                }

                // The helpers show the entity better than the bounds of its editor model
                if (!(hasHelpers && node.LayerName == EditorEntityNode.LayerName) && node is not SimpleBoxSceneNode and not SpriteSceneNode)
                {
                    AddBox(renderContext.Camera, updateContext.TextRenderer, node.Transform * toWorld, node.LocalBoundingBox, Color32.White, showSize: true);
                }

                // draw node name above the bounding box
                var position = bounds.Center;
                position.Z = bounds.Max.Z;

                updateContext.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
                {
                    Scale = 20f,
                    Text = nodeName,
                    CenterHorizontal = true,
                    TextOffset = SelectedNodeNameOffset
                }, renderContext.Camera, fixedScale: false);
            }

            lines.Upload(vertices.Lines);
            overlayLines.Upload(vertices.OverlayLines);
            faces.Upload(vertices.Faces);
            overlayFaces.Upload(vertices.OverlayFaces);

            vertices.Clear();
        }

        private void ClearBuffers()
        {
            lines.Clear();
            overlayLines.Clear();
            faces.Clear();
            overlayFaces.Clear();
        }

        private void RemoveAllLightProbeDebugGrid()
        {
            foreach (var node in selectedNodes)
            {
                node.LightProbeBinding?.RemoveDebugGridSpheres();
            }
        }

        /// <summary>Renders the selection overlay over the final image, after post processing.</summary>
        /// <param name="sceneDepth">The scene's resolved depth, to find what it hides.</param>
        public void Render(RenderTexture sceneDepth)
        {
            if (lines.VertexCount == 0 && overlayLines.VertexCount == 0)
            {
                return;
            }

            var shader = lines.Shader;
            shader.Use();
            shader.SetTexture((int)ReservedTextureSlots.SceneDepth, "g_tSceneDepth", sceneDepth);

            using var _ = GraphicsContext.RenderState.Scope(depthTest: false, depthWrite: false, blend: true);

            shader.SetUniform("g_bDepthTest", false);
            overlayFaces.Draw(0, overlayFaces.VertexCount, primitive: PrimitiveType.Triangles);

            // Faces pull further towards the camera, so they still show where they lie on the scene's surfaces
            shader.SetUniform("g_bDepthTest", true);
            shader.SetUniform("g_flDepthTolerance", 0.004f);
            faces.Draw(0, faces.VertexCount, primitive: PrimitiveType.Triangles);

            shader.SetUniform("g_flDepthTolerance", 0.001f);
            lines.Draw();

            shader.SetUniform("g_bDepthTest", false);
            overlayLines.Draw();
        }

        /// <summary>Deletes the GL objects.</summary>
        public void Delete()
        {
            lines.Delete();
            overlayLines.Delete();
            faces.Delete();
            overlayFaces.Delete();
        }

        /// <summary>Updates which debug overlays (cubemaps, light probes) are drawn based on the active render mode.</summary>
        /// <param name="mode">The render mode name from the viewer.</param>
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
