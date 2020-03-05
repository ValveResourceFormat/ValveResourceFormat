using System;
using System.Drawing;

namespace GUI.Controls
{
    public class ListViewItemClickEventArgs : EventArgs
    {
        public object Tag { get; }
        public Point Location { get; }

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
