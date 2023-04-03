using System;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    public interface IViewer
    {
        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input);

        public static void AddContentTab<T>(TabControl resTabs, string name, T content, bool preSelect = false)
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

            var control = new MonospaceTextBox
            {
                Text = extract.ReplaceLineEndings(),
            };

            var tab = new TabPage(name);
            tab.Controls.Add(control);
            resTabs.TabPages.Add(tab);

            if (preSelect)
            {
                resTabs.SelectTab(tab);
            }
        }
    }
}
