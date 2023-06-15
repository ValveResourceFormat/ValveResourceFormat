using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class Scene
    {
        public class UpdateContext
        {
            public float Timestep { get; }

            public UpdateContext(float timestep)
            {
                Timestep = timestep;
            }
        }

        public struct RenderContext
        {
            public Camera Camera { get; init; }
            public float Time { get; init; }
            public Matrix4x4? GlobalLightTransform { get; init; }
            public WorldLightingInfo LightingInfo { get; set; }
            public RenderPass RenderPass { get; set; }
            public Shader ReplacementShader { get; set; }
            public bool RenderToolsMaterials { get; init; }
        }

        public Camera MainCamera { get; set; }
        public float Time { get; set; }
        public SceneSky Sky { get; set; }
        public Matrix4x4? GlobalLightTransform { get; set; }
        public WorldLightingInfo LightingInfo { get; set; }
        public VrfGuiContext GuiContext { get; }
        public Octree<SceneNode> StaticOctree { get; }
        public Octree<SceneNode> DynamicOctree { get; }

        public bool ShowToolsMaterials { get; set; }

        public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);

        private readonly List<SceneNode> staticNodes = new();
        private readonly List<SceneNode> dynamicNodes = new();

        public Scene(VrfGuiContext context, float sizeHint = 32768)
        {
            GuiContext = context;
            StaticOctree = new Octree<SceneNode>(sizeHint);
            DynamicOctree = new Octree<SceneNode>(sizeHint);
        }

        public void Add(SceneNode node, bool dynamic)
        {
            if (dynamic)
            {
                dynamicNodes.Add(node);
                DynamicOctree.Insert(node, node.BoundingBox);
                node.Id = (uint)dynamicNodes.Count * 2 - 1;
            }
            else
            {
                staticNodes.Add(node);
                StaticOctree.Insert(node, node.BoundingBox);
                node.Id = (uint)staticNodes.Count * 2;
            }
        }

        public SceneNode Find(uint id)
        {
            if (id == 0)
            {
                return null;
            }

            if (id % 2 == 1)
            {
                var index = ((int)id + 1) / 2 - 1;

                if (index >= dynamicNodes.Count)
                {
                    return null;
                }

                return dynamicNodes[index];
            }
            else
            {
                var index = (int)id / 2 - 1;

                if (index >= staticNodes.Count)
                {
                    return null;
                }

                return staticNodes[index];
            }
        }

        public void Update(float timestep)
        {
            var updateContext = new UpdateContext(timestep);
            Time += timestep;

            foreach (var node in staticNodes)
            {
                node.Update(updateContext);
            }

            foreach (var node in dynamicNodes)
            {
                var oldBox = node.BoundingBox;
                node.Update(updateContext);
                DynamicOctree.Update(node, oldBox, node.BoundingBox);
            }
        }

        public void RenderWithCamera(Camera camera, Frustum cullFrustum = null)
        {
            var allNodes = StaticOctree.Query(cullFrustum ?? camera.ViewFrustum);
            allNodes.AddRange(DynamicOctree.Query(cullFrustum ?? camera.ViewFrustum));

            // Collect mesh calls
            var opaqueDrawCalls = new List<MeshBatchRenderer.Request>();
            var translucentDrawCalls = new List<MeshBatchRenderer.Request>();
            var looseNodes = new List<SceneNode>();
            foreach (var node in allNodes)
            {
                if (node is IRenderableMeshCollection meshCollection)
                {
                    foreach (var mesh in meshCollection.RenderableMeshes)
                    {
                        foreach (var call in mesh.DrawCallsOpaque)
                        {
                            opaqueDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                                Node = node,
                            });
                        }

                        foreach (var call in mesh.DrawCallsBlended)
                        {
                            translucentDrawCalls.Add(new MeshBatchRenderer.Request
                            {
                                Transform = node.Transform,
                                Mesh = mesh,
                                Call = call,
                                DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                                Node = node,
                            });
                        }
                    }
                }
                else if (node is SceneAggregate aggregate)
                {
                }
                else if (node is SceneAggregate.Fragment fragment)
                {
                    opaqueDrawCalls.Add(new MeshBatchRenderer.Request
                    {
                        Transform = fragment.Transform,
                        Mesh = fragment.RenderMesh,
                        Call = fragment.DrawCall,
                        DistanceFromCamera = (node.BoundingBox.Center - camera.Location).LengthSquared(),
                        Node = node,
                    });
                }
                else
                {
                    looseNodes.Add(node);
                }
            }

            // Sort loose nodes by distance from camera
            looseNodes.Sort((a, b) =>
                {
                    var aLength = (a.BoundingBox.Center - camera.Location).LengthSquared();
                    var bLength = (b.BoundingBox.Center - camera.Location).LengthSquared();
                    return bLength.CompareTo(aLength);
                });

            // Opaque render pass
            var renderContext = new RenderContext
            {
                Camera = camera,
                Time = Time,
                GlobalLightTransform = GlobalLightTransform,
                LightingInfo = LightingInfo,
                RenderPass = RenderPass.Opaque,
                RenderToolsMaterials = ShowToolsMaterials,
            };

            if (camera.Picker is not null)
            {
                if (camera.Picker.IsActive)
                {
                    camera.Picker.Render();
                    renderContext.ReplacementShader = camera.Picker.Shader;
                }
                else if (camera.Picker.DebugShader is not null)
                {
                    renderContext.ReplacementShader = camera.Picker.DebugShader;
                }
            }

            MeshBatchRenderer.Render(opaqueDrawCalls, renderContext);
            foreach (var node in looseNodes)
            {
                node.Render(renderContext);
            }

            // Translucent render pass, back to front for loose nodes
            renderContext.RenderPass = RenderPass.Translucent;

            MeshBatchRenderer.Render(translucentDrawCalls, renderContext);
            foreach (var node in Enumerable.Reverse(looseNodes))
            {
                node.Render(renderContext);
            }

            if (camera.Picker is not null && camera.Picker.IsActive)
            {
                camera.Picker.Finish();
                RenderWithCamera(camera, cullFrustum);
            }
        }

        public void SetEnabledLayers(HashSet<string> layers)
        {
            foreach (var renderer in AllNodes)
            {
                renderer.LayerEnabled = layers.Contains(renderer.LayerName);
            }

            StaticOctree.Clear();
            DynamicOctree.Clear();

            foreach (var node in staticNodes)
            {
                if (node.LayerEnabled)
                {
                    StaticOctree.Insert(node, node.BoundingBox);
                }
            }

            foreach (var node in dynamicNodes)
            {
                if (node.LayerEnabled)
                {
                    DynamicOctree.Insert(node, node.BoundingBox);
                }
            }
        }

        public void CalculateEnvironmentMaps()
        {
            if (LightingInfo.EnvMaps.Count == 0)
            {
                return;
            }

            var maxEnvMapArrayIndex = 1 + LightingInfo.EnvMaps.Max(x => x.Value.ArrayIndex);

            LightingInfo.EnvMapPositionsUniform = new float[maxEnvMapArrayIndex * 4];
            LightingInfo.EnvMapMinsUniform = new float[maxEnvMapArrayIndex * 4];
            LightingInfo.EnvMapMaxsUniform = new float[maxEnvMapArrayIndex * 4];

            foreach (var envMap in LightingInfo.EnvMaps.Values)
            {
                var nodes = StaticOctree.Query(envMap.BoundingBox);

                foreach (var node in nodes)
                {
                    node.EnvMaps.Add(envMap);
                    //node.CubeMapPrecomputedHandshake = envMap.HandShake;
                }

                var offsetFl = envMap.ArrayIndex * 4;

                LightingInfo.EnvMapPositionsUniform[offsetFl] = envMap.Transform.M41;
                LightingInfo.EnvMapPositionsUniform[offsetFl + 1] = envMap.Transform.M42;
                LightingInfo.EnvMapPositionsUniform[offsetFl + 2] = envMap.Transform.M43;
                LightingInfo.EnvMapPositionsUniform[offsetFl + 3] = envMap.ProjectionMode;

                LightingInfo.EnvMapMinsUniform[offsetFl] = envMap.BoundingBox.Min.X;
                LightingInfo.EnvMapMinsUniform[offsetFl + 1] = envMap.BoundingBox.Min.Y;
                LightingInfo.EnvMapMinsUniform[offsetFl + 2] = envMap.BoundingBox.Min.Z;

                LightingInfo.EnvMapMaxsUniform[offsetFl] = envMap.BoundingBox.Max.X;
                LightingInfo.EnvMapMaxsUniform[offsetFl + 1] = envMap.BoundingBox.Max.Y;
                LightingInfo.EnvMapMaxsUniform[offsetFl + 2] = envMap.BoundingBox.Max.Z;
            }

            foreach (var node in AllNodes)
            {
                if (!node.EnvMaps.Any())
                {
                    continue;
                }

                var envMaps = node.EnvMaps.OrderBy((envMap) =>
                {
                    return Vector3.Distance(node.BoundingBox.Center, envMap.BoundingBox.Center);
                }).ToList();

                node.CubeMapPrecomputedHandshake = envMaps.First().HandShake;
            }
        }
    }
}
