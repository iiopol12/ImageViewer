using System.IO;
using System.Windows;
using ImageViewer.Views;

namespace ImageViewer
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string? startupFile = null;
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (File.Exists(filePath))
                {
                    startupFile = filePath;
                }
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (!string.IsNullOrWhiteSpace(startupFile))
            {
                mainWindow.ShowInTaskbar = false;
                mainWindow.Opacity = 0;

                try
                {
                    await mainWindow.LoadStartupFileAsync(startupFile);
                }
                finally
                {
                    mainWindow.Show();
                    mainWindow.ShowInTaskbar = true;
                    mainWindow.Activate();
                }

                return;
            }

            mainWindow.Show();
        }
    }
}
