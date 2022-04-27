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
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using Windows.Storage;
using System.Threading;
using Windows.ApplicationModel.Core;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Microsoft.UI;
using System.Reflection;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace GUI2
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            tabView.Loaded += TabView_Loaded;
        }

        private void TabView_Loaded(object sender, RoutedEventArgs e)
        {
            var window = GetAppWindowForCurrentWindow();
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var coreTitleBar = window.TitleBar;
                coreTitleBar.ExtendsContentIntoTitleBar = true;
                tabView.SizeChanged += TabView_SizeChanged;
                CalculateTitleBar();
            }
            window.Title = "VRF - Source 2 Resource Viewer v" + Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        private void TabView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CalculateTitleBar();
        }
        private void CalculateTitleBar()
        {
            var transform = mainButton.TransformToVisual(null);
            var buttonOrigin = transform.TransformPoint(new Point(0, 0));

            var coreTitleBar = GetAppWindowForCurrentWindow().TitleBar;

            var x = (int)((buttonOrigin.X + mainButton.ActualWidth) * XamlRoot.RasterizationScale);
            var width = (int)((tabView.ActualWidth - x) * XamlRoot.RasterizationScale);
            var rect = new Windows.Graphics.RectInt32(x, 0, width, (int)(32 * XamlRoot.RasterizationScale));

            // WinSDK bug means ghost hitboxes remain if you don't clean them, this does cause a visual bug though :(
            // https://github.com/microsoft/WindowsAppSDK/issues/1626#issuecomment-1026936447
            coreTitleBar.ResetToDefault();
            coreTitleBar.ExtendsContentIntoTitleBar = true;
            coreTitleBar.SetDragRectangles(new Windows.Graphics.RectInt32[] { rect });
        }

        private async void OpenFile_Click(object sender, SplitButtonClickEventArgs e)
        {
            try
            {
                var filePicker = new FileOpenPicker();
                filePicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                filePicker.FileTypeFilter.Add("*");

                // WTF Microsoft
                var handle = WindowNative.GetWindowHandle(App.Window);
                InitializeWithWindow.Initialize(filePicker, handle);

                var files = await filePicker.PickMultipleFilesAsync();

                foreach (var file in files)
                {
                    VrfGlobalSingleton.OpenFile(file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private int tabCount;

        private void TabView_OnTabItemsChanged(object sender, IVectorChangedEventArgs args)
        {
            if (tabView.TabItems.Count != tabCount)
            {
                tabView.SelectedIndex = tabView.TabItems.Count - 1;
                tabCount = tabView.TabItems.Count;
            }
        }


        private static AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(App.Window);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }
    }
}
