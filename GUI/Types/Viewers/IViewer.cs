using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    interface IViewer
    {
        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream);

        public static TabPage AddContentTab<T>(TabControl resTabs, string name, T content, bool preSelect = false)
        {
            string extract = null;
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
                extract = content.ToString();
            }

            var control = new CodeTextBox(extract);
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
