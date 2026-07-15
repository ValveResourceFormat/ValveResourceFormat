using System.Windows.Forms;
using GUI.Types.Viewers;

namespace GUI.Controls;

// renders the UI agnostic ViewerContent model into winforms controls
static class ViewerContentPresenter
{
    public static void Present(TabPage container, ViewerContent content)
    {
        if (content is ViewerContent.Tabs tabs)
        {
            var tabControl = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            container.Controls.Add(tabControl);

            foreach (var tab in tabs.Items)
            {
                AddContentTab(tabControl, tab);
            }

            return;
        }

        container.Controls.Add(CreateControl(content));
    }

    public static TabPage AddContentTab(ThemedTabControl tabControl, string name, ViewerContent content, bool select = false)
    {
        return AddContentTab(tabControl, new ViewerTab(name, content, select));
    }

    public static TabPage AddContentTab(ThemedTabControl tabControl, ViewerTab tab)
    {
        var page = new ThemedTabPage(tab.Name);
        page.Controls.Add(CreateControl(tab.Content));
        tabControl.TabPages.Add(page);

        if (tab.Select)
        {
            tabControl.SelectTab(page);
        }

        return page;
    }

    private static Control CreateControl(ViewerContent content) => content switch
    {
        ViewerContent.Text text => CodeTextBox.Create(text.Content, text.Language, text.SourceMap),
        ViewerContent.LazyText lazy => CreateLazyText(lazy),
        ViewerContent.HexDump hex => CreateHexDump(hex),
        ViewerContent.Grid grid => CreateGrid(grid),
        _ => throw new NotSupportedException($"Unknown content type {content.GetType().Name}"),
    };

    private static System.ComponentModel.Design.ByteViewer CreateHexDump(ViewerContent.HexDump hex)
    {
        var control = new System.ComponentModel.Design.ByteViewer
        {
            Dock = DockStyle.Fill,
        };
        control.SetBytes(hex.Bytes);
        return control;
    }

    private static Control CreateLazyText(ViewerContent.LazyText lazy)
    {
        string text;

        try
        {
            text = lazy.GetContent();
        }
        catch (Exception e)
        {
            text = e.ToString();
        }

        return CodeTextBox.Create(text, lazy.Language);
    }

    private static DataGridView CreateGrid(ViewerContent.Grid grid) => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        DataSource = new BindingSource(grid.Rows, string.Empty),
        ScrollBars = ScrollBars.Both,
    };
}
