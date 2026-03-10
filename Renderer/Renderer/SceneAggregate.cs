using System.IO;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Scene node for instanced rendering of aggregated world geometry.
    /// </summary>
    public class SceneAggregate : SceneNode
    {
        /// <summary>Gets the shared renderable mesh for all fragments in this aggregate.</summary>
        public RenderableMesh RenderMesh { get; }

        /// <summary>Gets the list of drawable fragments that make up this aggregate.</summary>
        public List<Fragment> Fragments { get; private set; } = [];

        /// <summary>Gets or sets the byte offset into the indirect draw buffer for this aggregate's draws.</summary>
        public int IndirectDrawByteOffset { get; set; }

        /// <summary>Gets or sets the number of indirect draw commands for this aggregate.</summary>
        public int IndirectDrawCount { get; set; }

        /// <summary>Gets or sets the compaction buffer index used for GPU-driven draw count, or -1 if not compacted.</summary>
        public int CompactionIndex { get; set; } = -1;

        /// <summary>Gets or sets whether any fragment of this aggregate is visible this frame.</summary>
        public bool AnyChildrenVisible { get; internal set; }

        /// <summary>Gets the per-instance transform matrices used for instanced drawing.</summary>
        public List<OpenTK.Mathematics.Matrix3x4> InstanceTransforms { get; } = [];

        /// <summary>Gets or sets whether this aggregate can use GPU indirect drawing.</summary>
        public bool CanDrawIndirect { get; set; }

        /// <summary>Gets or sets the combined object type flags across all fragments (bitwise AND).</summary>
        public ObjectTypeFlags AllFlags { get; set; }

        /// <summary>Gets or sets the combined object type flags across all fragments (bitwise OR).</summary>
        public ObjectTypeFlags AnyFlags { get; set; }

        /// <summary>
        /// Single drawable fragment within an aggregate with independent bounds.
        /// </summary>
        public sealed class Fragment : SceneNode
        {
            /// <summary>Gets the aggregate that owns this fragment.</summary>
            public required SceneAggregate Parent { get; init; }

            /// <summary>Gets the shared renderable mesh used to issue this fragment's draw call.</summary>
            public required RenderableMesh RenderMesh { get; init; }

            /// <summary>Gets the specific draw call within the mesh that renders this fragment.</summary>
            public required DrawCall DrawCall { get; init; }

            /// <summary>Gets or sets the per-fragment tint color.</summary>
            public Vector4 Tint { get; set; } = Vector4.One;

            /// <inheritdoc/>
            public Fragment(Scene scene, SceneAggregate parent, AABB bounds) : base(scene)
            {
                Parent = parent;
                LocalBoundingBox = bounds;
                Name = parent.Name;
                LayerName = parent.LayerName;
            }
        }

        /// <summary>Initializes the scene aggregate, loading or resolving the mesh from the model.</summary>
        /// <param name="scene">Owning scene.</param>
        /// <param name="model">Model resource providing the embedded or referenced mesh.</param>
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

        /// <summary>Expands the aggregate's bounding box to cover the entire scene, preventing it from being frustum-culled.</summary>
        public void SetInfiniteBoundingBox()
        {
            LocalBoundingBox = new AABB(Vector3.NegativeInfinity, Vector3.PositiveInfinity);
        }

        /// <summary>Parses fragment data from the scene object and adds each fragment to the scene.</summary>
        /// <param name="aggregateSceneObject">KV3 object describing the aggregate's fragment list.</param>
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

            CanDrawIndirect = RenderMesh.DrawCallsOpaque.Count > 0;

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
                    CanDrawIndirect = false; // skip indirect draw path for instanced draws
                    fragment.Transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();
                }

                yield return fragment;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => RenderMesh.GetSupportedRenderModes();

#if DEBUG
        /// <inheritdoc/>
        public override void UpdateVertexArrayObjects() => RenderMesh.UpdateVertexArrayObjects();
#endif
    }
}
