using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Resources
{
    public class WorldNodeTest
    {
        private static KVObject Override(uint sceneObjectIndex, uint subSceneObject, uint drawCallIndex)
        {
            var value = KVObject.ListCollection();
            value["m_nSceneObjectIndex"] = new KVObject(sceneObjectIndex);
            value["m_nSubSceneObject"] = new KVObject(subSceneObject);
            value["m_nDrawCallIndex"] = new KVObject(drawCallIndex);
            return value;
        }

        [Test]
        public async Task ReadsWholeModelOverrides()
        {
            var materialOverride = Override(154, 0, uint.MaxValue);
            materialOverride["m_pMaterial"] = new KVObject("materials/clouds.vmat");

            var streamBinding = KVObject.ListCollection();
            streamBinding["m_hBuffer"] = new KVObject(0);
            var streamOverride = Override(3, 1, uint.MaxValue);
            streamOverride["m_extraBufferBinding"] = streamBinding;

            var materialOverrides = KVObject.Array();
            materialOverrides.Add(materialOverride);
            var streamOverrides = KVObject.Array();
            streamOverrides.Add(streamOverride);

            var data = KVObject.ListCollection();
            data["m_materialOverrides"] = materialOverrides;
            data["m_extraVertexStreamOverrides"] = streamOverrides;

            using var resource = new Resource();
            var node = new WorldNode { Resource = resource, Data = data };

            var parsedMaterialOverride = node.MaterialOverrides.Single();
            await Assert.That(parsedMaterialOverride.SceneObjectIndex).IsEqualTo(154U);
            await Assert.That(parsedMaterialOverride.DrawCallIndex).IsEqualTo(WorldNode.MaterialOverride.AllDrawCalls);
            await Assert.That(parsedMaterialOverride.IsWholeModel).IsTrue();
            await Assert.That(parsedMaterialOverride.Material).IsEqualTo("materials/clouds.vmat");

            var parsedStreamOverride = node.ExtraVertexStreamOverrides.Single();
            await Assert.That(parsedStreamOverride.SubSceneObject).IsEqualTo(1U);
            await Assert.That(parsedStreamOverride.DrawCallIndex).IsEqualTo(uint.MaxValue);
        }
    }
}
