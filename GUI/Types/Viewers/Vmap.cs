using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Viewers;

internal sealed class Vmap(VrfGuiContext guiContext) : IViewer
{
    private Datamodel.Datamodel? document;
    private GLVmapViewer? glViewer;
    private string? text;

    public static bool IsAccepted(uint magic, string fileName)
        => magic == BinaryKeyValues2.MAGIC && fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase);

    public async Task LoadAsync(Stream? input)
    {
        var stream = input ?? File.OpenRead(guiContext.FileName);
        try
        {
            document = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);
        }
        finally
        {
            stream.Close();
        }

        using var output = new MemoryStream();
        document.Save(output, "keyvalues2", 4);
        output.Position = 0;
        using var reader = new StreamReader(output);
        text = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (document.Root is not Datamodel.Element root)
        {
            return;
        }

        RendererContext? rendererContext = null;
        try
        {
            rendererContext = guiContext.CreateRendererContext();
            glViewer = new GLVmapViewer(guiContext, rendererContext, root);
            glViewer.InitializeLoad();
            rendererContext = null;
        }
        finally
        {
            rendererContext?.Dispose();
        }
    }

    public void Create(TabPage containerTabPage)
    {
        Debug.Assert(text != null);

        var tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
        };
        containerTabPage.Controls.Add(tabs);

        if (glViewer != null)
        {
            var viewportPage = new ThemedTabPage("VMAP");
            viewportPage.Controls.Add(glViewer.InitializeUiControls());
            tabs.TabPages.Add(viewportPage);
        }

        var dataPage = new ThemedTabPage("DataModel");
        dataPage.Controls.Add(CodeTextBox.Create(text, HighlightLanguage.None));
        tabs.TabPages.Add(dataPage);
        text = null;
    }

    public void NotifyVisible() => glViewer?.NotifyVisible();

    public void Dispose()
    {
        glViewer?.Dispose();
        document?.Dispose();
    }
}
