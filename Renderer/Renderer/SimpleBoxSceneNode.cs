using GUI.Utils;

namespace GUI.Types.Renderer
{
    class SimpleBoxSceneNode : ShapeSceneNode
    {
        public override bool IsTranslucent => false;

        public SimpleBoxSceneNode(Scene scene, Color32 color, Vector3 scale)
            : base(scene, scale / -2, scale / 2, color)
        {
        }
    }
}
