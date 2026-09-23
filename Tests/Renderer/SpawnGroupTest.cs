using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;

namespace Tests.Renderer
{
    /// <summary>How a map placed into another one is named, and where its entities end up.</summary>
    public class SpawnGroupTest
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
        /// A skybox_reference names the map in full, a CS2 ispointprefab entity and an
        /// info_spawngroup_load_unload name it relative to maps/ without an extension.
        /// </summary>
        [Test]
        [Arguments("maps/prefabs/de_dust2/de_dust2_skybox.vmap", "maps/prefabs/de_dust2/de_dust2_skybox")]
        [Arguments("prefabs/misc/team_select", "maps/prefabs/misc/team_select")]
        [Arguments("stages/lms_stage1", "maps/stages/lms_stage1")]
        [Arguments("maps\\stages\\lms_stage2.vmap_c", "maps/stages/lms_stage2")]
        public async Task NamesTheMapUnderMaps(string targetMapName, string expected)
        {
            await Assert.That(WorldLoader.GetSpawnGroupMapName(targetMapName)).IsEqualTo(expected);
        }

        /// <summary>An entity of a group loaded somewhere else than it was built is seen where it was placed.</summary>
        [Test]
        public async Task EntityOriginsFollowThePlacement()
        {
            var placement = Matrix4x4.CreateTranslation(100f, -200f, 32f);
            var group = new SpawnGroup("maps/stages/stage", CreateScene(), placement, []);

            await Assert.That(group.EntityOriginToWorld(new Vector3(1f, 2f, 3f))).IsEqualTo(new Vector3(101f, -198f, 35f));
        }
    }
}
