using System.Windows.Forms;

namespace GUI.Controls
{
    class DataGridViewDoubleBuffered : DataGridView
    {
        public DataGridViewDoubleBuffered() : base()
        {
            DoubleBuffered = true;
        }
    }
}
