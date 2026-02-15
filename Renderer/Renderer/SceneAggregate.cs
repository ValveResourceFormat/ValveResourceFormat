using System.IO;
using System.Linq;
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
        public List<Fragment> Fragments { get; private set; } = [];

        public int IndirectDrawByteOffset { get; set; }
        public int IndirectDrawCount { get; set; }

        public List<OpenTK.Mathematics.Matrix3x4> InstanceTransforms { get; } = [];
        public StorageBuffer? InstanceTransformsGpu { get; private set; }
        public bool HasTransforms { get; set; }

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

            public override bool IsSelected { get => base.IsSelected; set { base.IsSelected = value; Parent.IsSelected = value; } }

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
            Fragments.AddRange(CreateFragments(aggregateSceneObject));
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
                var drawBounds = drawCall.DrawBounds ?? throw new InvalidDataException("Draw call bounds must exist for all new format fragments");
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
    }
}
