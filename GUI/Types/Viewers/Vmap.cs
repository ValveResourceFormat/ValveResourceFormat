using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers;

internal sealed class Vmap(VrfGuiContext guiContext) : IViewer
{
    private Datamodel.Datamodel? document;
    private GLVmapViewer? glViewer;
    private EntityIOGraphViewer? entityIoGraphViewer;
    private List<EntityLump.Entity> entities = [];
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

        var mapEntities = ValveMapEntityReader.ReadAll(root);
        entities = mapEntities.Select(item => item.Entity).ToList();

        RendererContext? rendererContext = null;
        try
        {
            rendererContext = guiContext.CreateRendererContext();
            glViewer = new GLVmapViewer(guiContext, rendererContext, root, mapEntities);
            glViewer.InitializeLoad();
            rendererContext = null;
        }
        finally
        {
            rendererContext?.Dispose();
        }

        if (entities.Any(entity => entity.Connections is { Count: > 0 }))
        {
            try
            {
                rendererContext = guiContext.CreateRendererContext();
                entityIoGraphViewer = new EntityIOGraphViewer(guiContext, rendererContext, entities, glViewer.SelectAndFocusEntities);
                entityIoGraphViewer.InitializeLoad();
                rendererContext = null;
            }
            finally
            {
                rendererContext?.Dispose();
            }
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

        var entitiesPage = new ThemedTabPage("Entity List");
        Action<EntityLump.Entity>? selectEntity = glViewer == null ? null : glViewer.SelectAndFocusEntity;
        var entityViewer = new EntityViewer(guiContext, entities, selectEntity);
        entitiesPage.Controls.Add(entityViewer);
        tabs.TabPages.Add(entitiesPage);

        if (glViewer != null)
        {
            glViewer.ShowEntityInList = entity =>
            {
                tabs.SelectTab(entitiesPage);
                entityViewer.SelectEntity(entity);
            };
        }

        if (entityIoGraphViewer != null)
        {
            var graphPage = new ThemedTabPage("ENTITY I/O GRAPH");
            graphPage.Controls.Add(entityIoGraphViewer.InitializeUiControls());
            tabs.TabPages.Add(graphPage);
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
        entityIoGraphViewer?.Dispose();
        document?.Dispose();
    }
}
