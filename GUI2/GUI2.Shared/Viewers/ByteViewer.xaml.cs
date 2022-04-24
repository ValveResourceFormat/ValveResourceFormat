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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GUI2.Viewers
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ByteViewer : Page
    {
        public ByteViewer()
        {
            this.InitializeComponent();
            navView.SelectedItem = navView.MenuItems.OfType<NavigationViewItem>().First();
        }

        private VrfGuiContext Ctx { get; set; }

        private NavigationViewItem _lastItem;

        private bool SupportsText { get; set; }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is VrfGuiContext ctx)
            {
                Ctx = ctx;
                SupportsText = !ctx.ContainsNull;
            }
            else
            {
                throw new NotSupportedException("ByteViewer only supports navigation with Context");
            }
            base.OnNavigatedTo(e);
        }

        private void OnSelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
        {
            var item = args.SelectedItem as NavigationViewItem;
            if (item == null || item == _lastItem)
                return;


            var type = ("ByteViewer_" + item.Tag) switch
            {
                nameof(ByteViewer_Hex) => typeof(ByteViewer_Hex),
                nameof(ByteViewer_Text) => typeof(ByteViewer_Text),
                _ => throw new NotSupportedException("Unknown NavigationViewItem being navigated to."),
            };
            contentFrame.Navigate(type, Ctx);
            _lastItem = item;
        }
    }
}
