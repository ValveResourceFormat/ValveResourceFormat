using System.Windows.Forms;
using GUI.Types.Viewers;

namespace GUI.Controls;

// renders the UI agnostic ViewerContent model into winforms controls
static class ViewerContentPresenter
{
    public static void Present(TabPage container, ViewerContent content)
    {
        container.Controls.Add(CreateControl(content, out _));
    }

    public static TabPage AddContentTab(ThemedTabControl tabControl, string name, ViewerContent content, bool select = false)
    {
        return AddContentTab(tabControl, new ViewerTab(name, content, select));
    }

    public static TabPage AddContentTab(ThemedTabControl tabControl, ViewerTab tab)
    {
        var page = new ThemedTabPage(tab.Name);
        page.Controls.Add(CreateControl(tab.Content, out var contentFailed));
        tabControl.TabPages.Add(page);

        // Do not focus a tab whose content failed to produce, it only contains the error text
        if (tab.Select && !contentFailed)
        {
            tabControl.SelectTab(page);
        }

        return page;
    }

    private static Control CreateControl(ViewerContent content, out bool contentFailed)
    {
        contentFailed = false;

        switch (content)
        {
            case ViewerContent.Text text:
                return CodeTextBox.Create(text.Content, text.Language, text.SourceMap);

            case ViewerContent.LazyText lazy:
            {
                string producedText;

                try
                {
                    producedText = lazy.GetContent();
                }
                catch (Exception e)
                {
                    producedText = e.ToString();
                    contentFailed = true;
                }

                return CodeTextBox.Create(producedText, lazy.Language);
            }

            case ViewerContent.HexDump hex:
            {
                var control = new System.ComponentModel.Design.ByteViewer
                {
                    Dock = DockStyle.Fill,
                };
                control.SetBytes(hex.Bytes);
                return control;
            }

            case ViewerContent.Grid grid:
                return new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    DataSource = new BindingSource(grid.Rows, string.Empty),
                    ScrollBars = ScrollBars.Both,
                };

            case ViewerContent.Tabs tabs:
            {
                var tabControl = new ThemedTabControl
                {
                    Dock = DockStyle.Fill,
                };

                foreach (var tab in tabs.Items)
                {
                    AddContentTab(tabControl, tab);
                }

                return tabControl;
            }

            default:
                throw new NotSupportedException($"Unknown content type {content.GetType().Name}");
        }
    }
}
