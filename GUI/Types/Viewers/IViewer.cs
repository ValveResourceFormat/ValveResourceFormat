using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using static GUI.Controls.CodeTextBox;

namespace GUI.Types.Viewers
{
    interface IViewer
    {
        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream);

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
