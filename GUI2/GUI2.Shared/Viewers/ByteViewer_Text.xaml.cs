using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Text;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GUI2.Viewers
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ByteViewer_Text : Page
    {
        public ByteViewer_Text()
        {
            this.InitializeComponent();
            Loaded += ByteViewer_Text_Loaded;
        }

        private void ByteViewer_Text_Loaded(object sender, RoutedEventArgs e)
        {
            XamlRoot.Changed += XamlRoot_Changed;
            SetScrollHeight();
        }

        private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            SetScrollHeight();
        }
        private void SetScrollHeight()
        {
            var transform = scroller.TransformToVisual(null);
            var origin = transform.TransformPoint(new Point(0, 0));
            var height = XamlRoot.Size.Height - origin.Y;
            scroller.MaxHeight = height;
        }

        private string TextContent { get; set; }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is VrfGuiContext ctx)
            {
                TextContent = Encoding.UTF8.GetString(ctx.FileBytes);
            }
            else
            {
                throw new NotSupportedException("ByteViewer only supports navigation with Context");
            }
            base.OnNavigatedTo(e);
        }
    }
}
