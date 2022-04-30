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
using System.Collections.ObjectModel;
using ValveResourceFormat;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GUI2.Viewers
{

    public class ResourceTab
    {
        public string DisplayName { get; set; }
        public Type ContentPage { get; set; }
        public object Param { get; set; }
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// 
    public sealed partial class Resource : Page
    {
        public static bool IsAccepted(uint magic, ushort magicResourceVersion)
        {
            return magicResourceVersion == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public ObservableCollection<ResourceTab> Tabs { get; private set; } = new ObservableCollection<ResourceTab>();

        public Resource()
        {
            this.InitializeComponent();
        }
        private VrfGuiContext Ctx { get; set; }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is VrfGuiContext ctx)
            {
                Ctx = ctx;
                var resource = new ValveResourceFormat.Resource()
                {
                    FileName = ctx.FileName
                };
                resource.Read(new MemoryStream(ctx.FileBytes));

                Tabs.Clear();

                foreach (var block in resource.Blocks)
                {
                    Tabs.Add(new ResourceTab()
                    {
                        DisplayName = block.Type.ToString(),
                        ContentPage = typeof(Resource_Generic),
                        Param = block.ToString(),
                    });
                }
                if (Tabs.Count > 0)
                {
                    navView.SelectedItem = Tabs[0];
                }
            }
            else
            {
                throw new NotSupportedException("Resource only supports navigation with Context");
            }
            base.OnNavigatedTo(e);
        }

        private ResourceTab _lastItem;
        private void OnSelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
        {
            var item = args.SelectedItem as ResourceTab;
            if (item == null || item == _lastItem)
                return;
            contentFrame.Navigate(item.ContentPage, item.Param);
            _lastItem = item;
        }
    }
}
