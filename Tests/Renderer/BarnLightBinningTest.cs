using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    public class BarnLightBinningTest
    {
        private readonly List<IDisposable> harnessContexts = [];

        [After(HookType.Test)]
        public void DisposeHarnessContexts()
        {
            for (var i = harnessContexts.Count - 1; i >= 0; i--)
            {
                harnessContexts[i].Dispose();
            }

            harnessContexts.Clear();
        }

        private Scene CreateScene()
        {
            var fileLoader = new GameFileLoader(null, null);
            harnessContexts.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            harnessContexts.Add(context);

            var scene = new Scene(context);
            harnessContexts.Add(scene);

            return scene;
        }

        /// <summary>
        /// The camera looks down +X, the light sits behind it, so only a frame drawn without culling bins it.
        /// </summary>
        private static (Scene Scene, Camera Camera) CreateLightBehindCamera(Scene scene)
        {
            var light = new SceneLight(scene)
            {
                Entity = SceneLight.EntityType.Barn,
                Type = SceneLight.LightType.Spot,
                Range = 256f,
                PrecomputedFieldsValid = true,
                PrecomputedBounds = new AABB(new Vector3(-1100f, -100f, -100f), new Vector3(-900f, 100f, 100f)),
            };

            light.PlaceAt(Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateTranslation(-1000f, 0f, 0f));
            scene.LightingInfo.BarnLights.Add(light);

            var camera = new Camera();
            camera.SetFromQAngle(Vector3.Zero);
            camera.RecalculateMatrices();

            return (scene, camera);
        }

        [Test]
        public async Task CullsLightsOutsideTheView()
        {
            var (scene, camera) = CreateLightBehindCamera(CreateScene());

            scene.LightingInfo.BinBarnLights(camera, 4096, cull: true);

            await Assert.That(scene.LightingInfo.LightingData.NumBarnLights).IsEqualTo(0u);
            await Assert.That(scene.LightingInfo.ShadowMapper.ShadowCasters.Count).IsEqualTo(0);
        }

        // The prewarm frame draws with culling off, so every light has to reach the lit and shadow passes
        [Test]
        public async Task BinsLightsOutsideTheViewWithoutCulling()
        {
            var (scene, camera) = CreateLightBehindCamera(CreateScene());

            scene.LightingInfo.BinBarnLights(camera, 4096, cull: false);

            await Assert.That(scene.LightingInfo.LightingData.NumBarnLights).IsEqualTo(1u);
            await Assert.That(scene.LightingInfo.ShadowMapper.ShadowCasters.Count).IsEqualTo(1);
        }
    }
}
