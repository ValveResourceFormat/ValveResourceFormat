using System;
using System.Drawing;

namespace GUI.Controls
{
    public class ListViewItemClickEventArgs : EventArgs
    {
        public object Node { get; }
        public Point Location { get; }

        public ListViewItemClickEventArgs(object tag)
        {
            Node = tag;
        }

        public ListViewItemClickEventArgs(object tag, Point location)
        {
            Node = tag;
            Location = location;
        }
    }
}
