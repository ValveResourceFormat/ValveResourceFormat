using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Controls
{
    public class TreeViewFolder
    {
        public string Text { get; private set; }
        public int ItemCount { get; private set; }

        public TreeViewFolder(string text, int itemCount)
        {
            Text = text;
            ItemCount = itemCount;
        }
    }
}
