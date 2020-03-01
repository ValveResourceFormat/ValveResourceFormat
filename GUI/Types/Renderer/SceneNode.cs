using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.Renderer
{
    internal abstract class SceneNode
    {
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
        public string LayerName { get; set; }
        public bool LayerEnabled { get; set; } = true;
        public AABB BoundingBox => LocalBoundingBox.Transform(Transform);

        public AABB LocalBoundingBox { get; protected set; }
        public Scene Scene { get; }

        public SceneNode(Scene scene)
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
