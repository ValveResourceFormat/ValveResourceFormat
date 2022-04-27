using Microsoft.UI.Xaml.Controls;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace GUI2
{
    static class VrfGlobalSingleton
    {
        public static ObservableCollection<TabViewItem> Tabs { get; set; } = new ObservableCollection<TabViewItem>();

        public static void OpenFile(StorageFile file)
        {
            OpenFileInternal(file.Name, () => new VrfGuiContext().ProcessStorageFile(file));
        }
        public static void OpenFile(string filename, byte[] data, Package package)
        {
            OpenFileInternal(filename, () => new VrfGuiContext(filename, data, package).Process());
        }

        private static void OpenFileInternal(string filename, Func<Task<VrfGuiContext>> func)
        {
            var tab = new TabViewItem();
            tab.Header = filename;

            Frame frame = new();
            tab.Content = frame;
            frame.Navigate(typeof(LoadingPage));
            Tabs.Add(tab);

            var task = Task.Factory.StartNew(func);

            task.ContinueWith(
                t =>
                {
                    t.Exception?.Flatten().Handle(ex =>
                    {
                        frame.Navigate(typeof(ErrorPage), ex.ToString());

                        return false;
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());

            task.ContinueWith(
                t =>
                {
                    var result = t.Unwrap().Result;
                    frame.Navigate(result.XamlPage, result);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
