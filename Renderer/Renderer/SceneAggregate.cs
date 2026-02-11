using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Scene node for instanced rendering of aggregated world geometry.
    /// </summary>
    public class SceneAggregate : SceneNode
    {
        public RenderableMesh RenderMesh { get; }
        public List<Fragment> Fragments { get; private set; }

        public List<OpenTK.Mathematics.Matrix3x4> InstanceTransforms { get; } = [];
        public StorageBuffer? InstanceTransformsGpu { get; private set; }
        public bool HasTransforms { get; private set; }

        public ObjectTypeFlags AllFlags { get; set; }
        public ObjectTypeFlags AnyFlags { get; set; }

        /// <summary>
        /// Single drawable fragment within an aggregate with independent bounds.
        /// </summary>
        public sealed class Fragment : SceneNode
        {
            public required SceneAggregate Parent { get; init; }
            public required RenderableMesh RenderMesh { get; init; }
            public required DrawCall DrawCall { get; init; }

            public Vector4 Tint { get; set; } = Vector4.One;

            public Fragment(Scene scene, SceneAggregate parent, AABB bounds) : base(scene)
            {
                Parent = parent;
                LocalBoundingBox = bounds;
                Name = parent.Name;
                LayerName = parent.LayerName;
            }
        }

        public SceneAggregate(Scene scene, Model model)
            : base(scene)
        {
            var embeddedMeshes = model.GetEmbeddedMeshesAndLoD().ToList();

            // TODO: Perhaps use ModelSceneNode.LoadMeshes
            if (embeddedMeshes.Count != 0)
            {
                RenderMesh = new RenderableMesh(embeddedMeshes.First().Mesh, 0, Scene, model, isAggregate: true);

                if (embeddedMeshes.Count > 1)
                {
                    throw new NotImplementedException("More than one embedded mesh");
                }
            }
            else
            {
                var refMeshes = model.GetReferenceMeshNamesAndLoD().Where(m => (m.LoDMask & 1) != 0).ToList();
                var refMesh = refMeshes.First();

                if (refMeshes.Count > 1)
                {
                    throw new NotImplementedException("More than one referenced mesh");
                }

                var newResource = Scene.RendererContext.FileLoader.LoadFileCompiled(refMesh.MeshName);

                if (newResource == null || newResource.DataBlock is not Mesh meshData)
                {
                    throw new InvalidDataException($"Failed to load {refMesh.MeshName}");
                }

                RenderMesh = new RenderableMesh(meshData, refMesh.MeshIndex, Scene, model, isAggregate: true);
            }

            LocalBoundingBox = RenderMesh.BoundingBox;
        }

        public void SetInfiniteBoundingBox()
        {
            LocalBoundingBox = new AABB(Vector3.NegativeInfinity, Vector3.PositiveInfinity);
        }

        public void LoadFragments(KVObject aggregateSceneObject)
        {
            Fragments = CreateFragments(aggregateSceneObject).ToList();
            foreach (var fragment in Fragments)
            {
                Scene.Add(fragment, false);
            }
        }

        private IEnumerable<Fragment> CreateFragments(KVObject aggregateSceneObject)
        {
            var aggregateMeshes = aggregateSceneObject.GetArray("m_aggregateMeshes");

            // Aperture Desk Job goes from draw call -> aggregate mesh
            if (aggregateMeshes.Length > 0 && !aggregateMeshes[0].ContainsKey("m_nDrawCallIndex"))
            {
                foreach (var drawCall in RenderMesh.DrawCallsOpaque)
                {
                    var fragmentData = aggregateMeshes[drawCall.MeshId];
                    var lightProbeVolumePrecomputedHandshake = fragmentData.GetInt32Property("m_nLightProbeVolumePrecomputedHandshake");
                    var worldBounds = fragmentData.GetArray("m_vWorldBounds");
                    var flags = fragmentData.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);

                    drawCall.DrawBounds = new AABB(worldBounds[0].ToVector3(), worldBounds[1].ToVector3());
                    var fragment = new Fragment(Scene, this, drawCall.DrawBounds.Value)
                    {
                        DrawCall = drawCall,
                        RenderMesh = RenderMesh,
                        Parent = this,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                        Flags = flags,
                    };

                    yield return fragment;
                }

                yield break;
            }

            var transformIndex = 0;
            var fragmentTransforms = aggregateSceneObject.GetArray("m_fragmentTransforms");

            // CS2 goes from aggregate mesh -> draw call (many meshes can share one draw call)
            foreach (var fragmentData in aggregateMeshes)
            {
                var lightProbeVolumePrecomputedHandshake = fragmentData.GetInt32Property("m_nLightProbeVolumePrecomputedHandshake");
                var drawCallIndex = fragmentData.GetInt32Property("m_nDrawCallIndex");
                var drawCall = RenderMesh.DrawCallsOpaque[drawCallIndex];
                var drawBounds = drawCall.DrawBounds ?? RenderMesh.BoundingBox;
                var tintColor = fragmentData.GetSubCollection("m_vTintColor").ToVector3();
                var flags = fragmentData.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);
                var lodGroupMask = fragmentData.GetUInt32Property("m_nLODGroupMask");

                if (lodGroupMask > 1)
                {
                    continue;
                }

                var fragment = new Fragment(Scene, this, drawBounds)
                {
                    DrawCall = drawCall,
                    RenderMesh = RenderMesh,
                    Tint = new Vector4(tintColor / 255f, 1f),
                    Parent = this,
                    LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                    Flags = flags,
                };

                if (fragmentData.GetProperty<bool>("m_bHasTransform") == true)
                {
                    HasTransforms = true; // skip indirect draw path for instanced draws
                    fragment.Transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();
                }

                yield return fragment;
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (InstanceTransforms.Count > 0 && InstanceTransformsGpu == null)
            {
                InstanceTransformsGpu = new StorageBuffer(ReservedBufferSlots.Transforms);
                InstanceTransformsGpu.Create(InstanceTransforms);
            }
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderMesh.GetSupportedRenderModes();

#if DEBUG
        public override void UpdateVertexArrayObjects() => RenderMesh.UpdateVertexArrayObjects();
#endif

        public float GetProjectedSizeWeight(Camera camera)
        {
            // Calculate total projected screen-space area of all fragments
            var viewProjection = camera.ViewProjectionMatrix;
            var totalProjectedArea = 0f;

            foreach (var fragment in Fragments)
            {
                var bbox = fragment.BoundingBox;

                // Get 8 corners of the bounding box
                Span<Vector3> corners =
                [
                    new Vector3(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
                    new Vector3(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                    new Vector3(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                    new Vector3(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
                    new Vector3(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                    new Vector3(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                    new Vector3(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
                    new Vector3(bbox.Max.X, bbox.Max.Y, bbox.Max.Z),
                ];

                // Project corners to screen space and find min/max
                var minX = float.PositiveInfinity;
                var maxX = float.NegativeInfinity;
                var minY = float.PositiveInfinity;
                var maxY = float.NegativeInfinity;
                var anyBehindCamera = false;

                for (var i = 0; i < 8; i++)
                {
                    var projected = Vector4.Transform(corners[i], viewProjection);

                    // Check if behind camera
                    if (projected.W <= 0)
                    {
                        anyBehindCamera = true;
                        continue;
                    }

                    // Perspective divide to get NDC coordinates [-1, 1]
                    var ndcX = projected.X / projected.W;
                    var ndcY = projected.Y / projected.W;

                    minX = MathF.Min(minX, ndcX);
                    maxX = MathF.Max(maxX, ndcX);
                    minY = MathF.Min(minY, ndcY);
                    maxY = MathF.Max(maxY, ndcY);
                }

                // If entirely behind camera, skip this fragment
                if (anyBehindCamera && float.IsInfinity(minX))
                {
                    continue;
                }

                // Clamp to screen bounds [-1, 1] in NDC
                minX = Math.Clamp(minX, -1f, 1f);
                maxX = Math.Clamp(maxX, -1f, 1f);
                minY = Math.Clamp(minY, -1f, 1f);
                maxY = Math.Clamp(maxY, -1f, 1f);

                // Calculate screen-space area in NDC units (0-4 range, where 4 = full screen)
                var width = maxX - minX;
                var height = maxY - minY;
                totalProjectedArea += width * height;
            }

            // larger area = render first
            return totalProjectedArea;
        }
    }
}
