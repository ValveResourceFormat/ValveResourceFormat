using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Controls
{
    public class ListViewItemClickEventArgs : EventArgs
    {
        public object Tag { get; private set; }
        public Point Location { get; private set; }

        public ListViewItemClickEventArgs(object tag)
        {
            Tag = tag;
        }

        public ListViewItemClickEventArgs(object tag, Point location)
        {
            Tag = tag;
            Location = location;
        }
    }
}
