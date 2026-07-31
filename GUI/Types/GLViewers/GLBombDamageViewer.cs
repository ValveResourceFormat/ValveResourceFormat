using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;

namespace GUI.Types.GLViewers
{
    class GLBombDamageViewer : GLSceneLayerViewer
    {
        private readonly BombDamage bombDamageData;

        public GLBombDamageViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, BombDamage bombDamageData)
            : base(vrfGuiContext, rendererContext)
        {
            this.bombDamageData = bombDamageData;
        }

        protected override string LayersControlName => "Bombsites";

        // Only enable the first bombsite by default to avoid overlapping visualizations
        protected override bool IsLayerEnabledByDefault(int index) => index == 0;

        protected override void LoadScene()
        {
            CS2BombDamageSceneNode.AddBakedBombDamageToScene(bombDamageData, Scene);
        }
    }
}
