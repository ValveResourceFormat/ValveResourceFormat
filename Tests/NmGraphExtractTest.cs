using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests;

public class NmGraphExtractTest
{
    private const string Animgraph2RootPath = "Files/Animgraph2";
    private const string ViewmodelGunPath = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph";

    private sealed class TestFileLoader(string rootPath) : IFileLoader
    {
        public Resource? LoadFile(string file)
        {
            var fullPath = Path.Combine(rootPath, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var resource = new Resource();
            resource.Read(fullPath);
            return resource;
        }

        public Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, GameFileLoader.CompiledFileSuffix));

        public ValveResourceFormat.CompiledShader.ShaderCollection? LoadShader(string shaderName) => null;
    }

    [Test]
    public void ExtractNmGraphViewmodelGunBatch()
    {
        var files = Directory.GetFiles(Path.Combine(Animgraph2RootPath, "animation/graphs/viewmodel"), "viewmodel_gun.vnmgraph*.vnmgraph_c");

        Assert.That(files, Has.Length.GreaterThan(1));
        Assert.Multiple(() =>
        {
            foreach (var file in files)
            {
                using var resource = new Resource();
                resource.Read(file);

                Assert.DoesNotThrow(() => FileExtract.Extract(resource, new NullFileLoader()), file);
            }
        });
    }

    [Test]
    public void ExtractNmGraphViewmodelGunBuildsVariationOverrides()
    {
        var rootPath = Path.GetFullPath(Animgraph2RootPath);
        using var resource = new Resource();
        resource.Read(Path.Combine(rootPath, string.Concat(ViewmodelGunPath.Replace('/', Path.DirectorySeparatorChar), GameFileLoader.CompiledFileSuffix)));

        var editInfo = resource.GetBlockByType(BlockType.RED2) as ResourceEditInfo2;
        Assert.That(editInfo, Is.Not.Null);
        Assert.That(editInfo!.ChildResourceList, Does.Contain("animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph"));

        var content = FileExtract.Extract(resource, new TestFileLoader(rootPath));
        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("m_variationHierarchy = "));
            Assert.That(text, Does.Contain("m_variationID = \"ak47\""));
        });
    }

    [Test]
    public void ExtractNmGraphDecodesIdEventConditionFlags()
    {
        static object InvokeRules(long flags)
        {
            var rules = KVObject.Collection();
            rules.Add("m_flags", flags);

            var node = KVObject.Collection();
            node.Add("m_eventConditionRules", rules);

            var method = typeof(NmGraphExtract).GetMethod("GetEventConditionRules", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return method!.Invoke(null, [node])!;
        }

        static object? GetProperty(object value, string propertyName)
            => value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);

        var searchAll = InvokeRules(272);
        var searchAnim = InvokeRules(144);
        var searchAnimAndLimit = InvokeRules(145);
        var searchAnimAndOperatorAnd = InvokeRules(160);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty(searchAll, "SearchRule"), Is.EqualTo("SearchAll"));
            Assert.That(GetProperty(searchAll, "Operator"), Is.EqualTo("Or"));
            Assert.That(GetProperty(searchAll, "LimitSearchToSourceState"), Is.EqualTo(false));
            Assert.That(GetProperty(searchAll, "IgnoreInactiveBranchEvents"), Is.EqualTo(false));

            Assert.That(GetProperty(searchAnim, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnim, "Operator"), Is.EqualTo("Or"));
            Assert.That(GetProperty(searchAnim, "LimitSearchToSourceState"), Is.EqualTo(false));

            Assert.That(GetProperty(searchAnimAndLimit, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnimAndLimit, "LimitSearchToSourceState"), Is.EqualTo(true));

            Assert.That(GetProperty(searchAnimAndOperatorAnd, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnimAndOperatorAnd, "Operator"), Is.EqualTo("And"));
        });
    }
}
