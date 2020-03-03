using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GUI.Types.Renderer
{
    internal abstract class SceneNode
    {
        public Matrix4x4 Transform
        {
            get => transform;
            set
            {
                transform = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        public string LayerName { get; set; }
        public bool LayerEnabled { get; set; } = true;
        public AABB BoundingBox { get; private set; }
        public AABB LocalBoundingBox
        {
            get => localBoundingBox;
            protected set
            {
                localBoundingBox = value;
                BoundingBox = LocalBoundingBox.Transform(transform);
            }
        }

        public Scene Scene { get; }

        private AABB localBoundingBox;
        private Matrix4x4 transform = Matrix4x4.Identity;

        protected SceneNode(Scene scene)
        {
            Scene = scene;
        }

        public abstract void Update(Scene.UpdateContext context);
        public abstract void Render(Scene.RenderContext context);

        public virtual IEnumerable<string> GetSupportedRenderModes() => Enumerable.Empty<string>();
        public virtual void SetRenderMode(string mode)
        {
        }
    }
}
