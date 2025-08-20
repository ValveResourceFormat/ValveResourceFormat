using GUI.Utils;

namespace GUI.Types.Renderer
{
    internal class PointTextSceneNode : SceneNode
    {
        private readonly string text;
        private readonly Color32 textColor;
        private readonly float textSize;

        public PointTextSceneNode(Scene scene) : base(scene)
        {
            // Extract text properties from entity data
            if (EntityData != null)
            {
                text = EntityData.GetProperty<string>("text") ?? "Missing Text";
                
                // Get color property as Vector3 and convert to Color32
                var colorVec = EntityData.ContainsKey("textcolor") 
                    ? EntityData.GetColor32Property("textcolor") 
                    : Vector3.One;
                textColor = new Color32(colorVec.X, colorVec.Y, colorVec.Z, 1.0f);
                
                textSize = EntityData.GetPropertyUnchecked<float>("textsize", 16.0f);
            }
            else
            {
                text = "Missing Text";
                textColor = Color32.White;
                textSize = 16.0f;
            }

            // Set a small bounding box for the text entity
            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            // Get the world position from the transform
            var position = Transform.Translation;

            // Render the text as a billboard at the entity's position using the viewer's TextRenderer
            context.View.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
            {
                Text = text,
                Scale = textSize,
                Color = textColor,
                CenterVertical = true,
                CenterHorizontal = true,
            });
        }

        public override void Update(Scene.UpdateContext context)
        {
            // No updates needed for text entities
        }
    }
}