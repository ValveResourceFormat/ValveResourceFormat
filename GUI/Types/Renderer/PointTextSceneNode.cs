using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    internal class PointTextSceneNode : SceneNode
    {
        private readonly string text;
        private readonly Color32 textColor;
        private readonly float textSize;
        //private readonly int orientation;

        public PointTextSceneNode(Scene scene, EntityLump.Entity entity) : base(scene)
        {
            text = entity.GetProperty<string>("message", string.Empty);
            textColor = Color32.FromVector4(new Vector4(entity.GetColor32Property("color"), 1));
            textSize = entity.GetPropertyUnchecked<float>("textsize", 10.0f);
            // orientation = entity.GetPropertyUnchecked<int>("orientation", 0);

            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
            EntityData = entity;
        }

        public override void Render(Scene.RenderContext context)
        {
            // Get the world position from the transform
            var position = Transform.Translation;
            var request = new TextRenderer.TextRenderRequest
            {
                Text = text,
                Scale = textSize,
                Color = textColor,
                CenterVertical = true,
                CenterHorizontal = true,
            };

            // todo: depth test
            // todo: custom projection based on orientation
            // 0 = stationary, 1 = billboard, 2 = horizontal billboard

            context.View.TextRenderer.AddTextBillboard(position, request);
        }
    }
}
