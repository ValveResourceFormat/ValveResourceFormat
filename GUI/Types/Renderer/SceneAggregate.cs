using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    class SceneAggregate : SceneNode
    {
        public RenderableMesh RenderMesh { get; }

        internal sealed class Fragment : SceneNode
        {
            public SceneNode Parent { get; init; }
            public RenderableMesh RenderMesh { get; init; }
            public DrawCall DrawCall { get; init; }

            public Vector4 Tint { get; set; } = Vector4.One;

            public Fragment(Scene scene, SceneNode parent, AABB bounds) : base(scene)
            {
                Parent = parent;
                LocalBoundingBox = bounds;
                Name = parent.Name;
                LayerName = parent.LayerName;
            }

            public override void Render(Scene.RenderContext context) { }
            public override void Update(Scene.UpdateContext context) { }
        }

        public SceneAggregate(Scene scene, Model model)
            : base(scene)
        {
            var embeddedMeshes = model.GetEmbeddedMeshesAndLoD().ToList();

            /// TODO: Perhaps use <see cref="ModelSceneNode.LoadMeshes">
            if (embeddedMeshes.Count != 0)
            {
                RenderMesh = new RenderableMesh(embeddedMeshes.First().Mesh, 0, Scene, model);

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

                var newResource = Scene.GuiContext.LoadFileCompiled(refMesh.MeshName);
                if (newResource == null)
                {
                    return;
                }

                RenderMesh = new RenderableMesh((Mesh)newResource.DataBlock, refMesh.MeshIndex, Scene, model);
            }

            LocalBoundingBox = RenderMesh.BoundingBox;
        }

        public IEnumerable<Fragment> CreateFragments(KVObject aggregateSceneObject)
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
                    drawCall.DrawBounds = new AABB(worldBounds[0].ToVector3(), worldBounds[1].ToVector3());
                    var fragment = new Fragment(Scene, this, drawCall.DrawBounds.Value)
                    {
                        DrawCall = drawCall,
                        RenderMesh = RenderMesh,
                        Parent = this,
                        LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
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

                var fragment = new Fragment(Scene, this, drawBounds)
                {
                    DrawCall = drawCall,
                    RenderMesh = RenderMesh,
                    Tint = new Vector4(tintColor / 255f, 1f),
                    Parent = this,
                    LightProbeVolumePrecomputedHandshake = lightProbeVolumePrecomputedHandshake,
                };

                if (fragmentData.GetProperty<bool>("m_bHasTransform") == true)
                {
                    fragment.Transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();
                }

                yield return fragment;
            }
        }

        public override void Render(Scene.RenderContext context)
        {
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderMesh.GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
            RenderMesh.SetRenderMode(renderMode);
        }

#if DEBUG
        public override void UpdateVertexArrayObjects() => RenderMesh.UpdateVertexArrayObjects();
#endif
    }
}
