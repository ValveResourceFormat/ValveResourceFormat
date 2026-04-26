using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests;

public class NmGraphExtractTest
{
    [Test]
    public void ExtractNmGraphDocument()
    {
        using var resource = new Resource();
        resource.Read("Files/viewmodel_inspects.vnmgraph+ak47.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocument\""));
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocStateMachineNode\""));
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocParameterizedClipSelectorNode\""));
            Assert.That(text, Does.Contain("lookat01_ak.vnmclip"));
            Assert.That(text, Does.Contain("m_ID = \"ak47\""));
        });
    }

    [Test]
    public void ExtractAllAnimgraph2Documents()
    {
        var files = Directory.GetFiles("Files/Animgraph2", "*.vnmgraph_c", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .ToArray();

        Assert.That(files, Is.Not.Empty);

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

}
