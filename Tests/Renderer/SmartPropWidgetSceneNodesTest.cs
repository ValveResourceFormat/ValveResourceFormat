using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    public class SmartPropWidgetSceneNodesTest
    {
        private const float Tolerance = 1e-3f;

        private static Matrix4x4 IdentityWorld(Vector3 translation) => Matrix4x4.CreateTranslation(translation);

        private static Matrix4x4 YawWorld(float degrees, Vector3 translation)
            => EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, degrees, 0f)) * Matrix4x4.CreateTranslation(translation);

    }
}
