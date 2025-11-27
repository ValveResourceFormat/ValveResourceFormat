using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using static GUI.Controls.CodeTextBox;

namespace GUI.Types.Viewers
{
    interface IViewer
    {
        public Task LoadAsync(Stream stream);
        public void Create(TabPage containerTabPage);

        public static TabPage AddContentTab<T>(TabControl resTabs, string name, T content, bool preSelect = false, HighlightLanguage highlightSyntax = HighlightLanguage.Default)
        {
            var extract = string.Empty;
            if (content is Func<string> exceptionless)
            {
                try
                {
                    extract = exceptionless();
                }
                catch (Exception e)
                {
                    extract = e.ToString();
                    preSelect = false;
                }
            }
            else
            {
                extract = content?.ToString() ?? string.Empty;
            }

            var control = CodeTextBox.Create(extract, highlightSyntax);
            var tab = new TabPage(name);
            tab.Controls.Add(control);
            resTabs.TabPages.Add(tab);

            if (preSelect)
            {
                resTabs.SelectTab(tab);
            }

            return tab;
        }
    }
}
