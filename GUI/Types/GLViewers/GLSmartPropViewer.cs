using System.Diagnostics;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    class GLSmartPropViewer : GLSingleNodeViewer
    {
        private readonly SmartProp smartProp;

        public GLSmartPropViewer(VrfGuiContext guiContext, SmartProp smartProp) : base(guiContext)
        {
            this.smartProp = smartProp;
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            var children = smartProp.Data.GetArray("m_Children");

            foreach (var child in children)
            {
                var className = child.GetStringProperty("_class");

                switch (className)
                {
                    case "CSmartPropElement_Model":
                        {
                            using var resource = GuiContext.LoadFileCompiled(child.GetStringProperty("m_sModelName"));
                            var model = (Model?)resource.DataBlock;
                            Debug.Assert(model != null);

                            var modelSceneNode = new ModelSceneNode(Scene, model);
                            Scene.Add(modelSceneNode, true);

                            break;
                        }
                    case "CSmartPropElement_SmartProp":
                        {
                            // TODO: m_sSmartProp - create SmartPropSceneNode?
                            break;
                        }
                    case "CSmartPropElement_Group":
                    case "CSmartPropElement_PickOne":
                        {
                            var pickOneChildren = child.GetArray("m_Children");

                            // TODO: This probably should recurse into parent smartprop loader
                            foreach (var pickOneChild in pickOneChildren)
                            {
                                var pickOneClass = pickOneChild.GetStringProperty("_class");

                                if (pickOneClass != "CSmartPropElement_Model")
                                {
                                    Log.Warn(nameof(GLSingleNodeViewer), $"Unhandled smart prop class {className}");
                                    continue;
                                }

                                using var resource = GuiContext.LoadFileCompiled(pickOneChild.GetStringProperty("m_sModelName"));
                                var model = (Model?)resource.DataBlock;
                                Debug.Assert(model != null);

                                var modelSceneNode = new ModelSceneNode(Scene, model);
                                Scene.Add(modelSceneNode, true);
                            }

                            break;
                        }
                    default:
                        Log.Warn(nameof(GLSingleNodeViewer), $"Unhandled smart prop class {className}");
                        break;
                }
            }
        }
    }
}
