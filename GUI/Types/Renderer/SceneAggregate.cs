
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class SceneAggregate : SceneNode
    {
        private Model Model { get; }
        public RenderableMesh RenderMesh { get; }

        internal class Fragment : SceneNode
        {
            public SceneNode Parent { get; init; }
            public RenderableMesh RenderMesh { get; init; }
            public DrawCall DrawCall { get; init; }

            /// <summary>
            /// In the format of 255,255,255
            /// </summary>
            public Vector3 Tint { get; set; }

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
            Model = model;
            RenderMesh = new RenderableMesh(Model.GetEmbeddedMeshesAndLoD().First().Mesh, 0, Scene.GuiContext);
            LocalBoundingBox = RenderMesh.BoundingBox;
        }

        public IEnumerable<Fragment> CreateFragments(IKeyValueCollection[] aggregateMeshes)
        {
            foreach (var fragmentData in aggregateMeshes)
            {
                var drawCallIndex = fragmentData.GetInt32Property("m_nDrawCallIndex");
                var drawCall = RenderMesh.DrawCallsOpaque[drawCallIndex];
                var drawBounds = RenderMesh.DrawCallsOpaque[drawCallIndex].DrawBounds ?? RenderMesh.BoundingBox;
                var fragment = new Fragment(Scene, this, drawBounds)
                {
                    Tint = fragmentData.GetSubCollection("m_vTintColor").ToVector3(),
                    DrawCall = drawCall,
                    RenderMesh = RenderMesh,
                    Parent = this,
                };

                yield return fragment;
            }
        }

        public override void Render(Scene.RenderContext context)
        {
        }

        public override void Update(Scene.UpdateContext context)
        {
            RenderMesh.Update(context.Timestep);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderMesh.GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
            RenderMesh.SetRenderMode(renderMode);
        }
    }
}